"""
CDK Custom Resource handler — provisions RabbitMQ topology on Amazon MQ.

Called during `cdk deploy` to create exchanges, queues, bindings, and policies
via the RabbitMQ Management HTTP API (port 443 on Amazon MQ).

Idempotent:
  - PUT exchanges/queues → no-op if already exists with same config
  - POST bindings → no-op for identical (source, dest, routing_key, args)
  - PUT policies → overwrites cleanly

On Delete: does nothing (queues/exchanges are preserved).
"""

import json
import urllib.request
import urllib.parse
import urllib.error
import ssl
import base64
import os

import boto3


def on_event(event, context):
    """Entry point for the CDK Provider framework."""
    print(f"RequestType: {event['RequestType']}")
    print(f"ResourceProperties keys: {list(event.get('ResourceProperties', {}).keys())}")

    request_type = event["RequestType"]
    props = event["ResourceProperties"]
    physical_id = "rabbitmq-topology-v1"

    if request_type == "Delete":
        print("Delete request — preserving all queues and exchanges")
        return {"PhysicalResourceId": physical_id}

    # ── Retrieve broker credentials from Secrets Manager ──
    region = props.get("Region", os.environ.get("AWS_REGION", "us-east-2"))
    secret_arn = props["SecretArn"]
    broker_host = props["BrokerHost"]
    topology = json.loads(props["Topology"])

    sm = boto3.client("secretsmanager", region_name=region)
    secret_value = json.loads(
        sm.get_secret_value(SecretId=secret_arn)["SecretString"]
    )
    username = secret_value.get("username") or secret_value.get("Username")
    password = secret_value.get("password") or secret_value.get("Password")

    if not username or not password:
        raise ValueError(
            f"Could not extract credentials from secret. Keys found: {list(secret_value.keys())}"
        )

    # ── Management API helpers ──
    base_url = f"https://{broker_host}/api"
    vhost = "%2F"  # URL-encoded "/"
    auth = base64.b64encode(f"{username}:{password}".encode()).decode()
    ctx = ssl.create_default_context()

    def api(method, path, body=None):
        url = f"{base_url}{path}"
        data = json.dumps(body).encode("utf-8") if body else None
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        req.add_header("Authorization", f"Basic {auth}")
        try:
            with urllib.request.urlopen(req, context=ctx, timeout=15) as resp:
                resp_body = resp.read().decode("utf-8", errors="replace")
                print(f"  {method} {path} → {resp.status}")
                return resp.status
        except urllib.error.HTTPError as e:
            body_text = e.read().decode("utf-8", errors="replace")
            # 204 = no content (success for PUT), 201 = created
            if e.code in (200, 201, 204):
                print(f"  {method} {path} → {e.code} (ok)")
                return e.code
            print(f"  {method} {path} → {e.code}: {body_text}")
            raise

    # ── Provision topology ──
    counts = {"exchanges": 0, "queues": 0, "bindings": 0, "policies": 0}

    # Exchanges (PUT is idempotent — safe for existing + new)
    for ex in topology.get("exchanges", []):
        name_enc = urllib.parse.quote(ex["name"], safe="")
        print(f"Exchange: {ex['name']}")
        api("PUT", f"/exchanges/{vhost}/{name_enc}", {
            "type": ex.get("type", "direct"),
            "durable": ex.get("durable", True),
            "auto_delete": ex.get("auto_delete", False),
            "arguments": ex.get("arguments", {}),
        })
        counts["exchanges"] += 1

    # Queues (PUT is idempotent if arguments match existing queue)
    for q in topology.get("queues", []):
        name_enc = urllib.parse.quote(q["name"], safe="")
        print(f"Queue: {q['name']}")
        api("PUT", f"/queues/{vhost}/{name_enc}", {
            "durable": q.get("durable", True),
            "auto_delete": q.get("auto_delete", False),
            "arguments": q.get("arguments", {}),
        })
        counts["queues"] += 1

    # Bindings (POST — idempotent for same source/dest/routing_key/args tuple)
    for b in topology.get("bindings", []):
        src = urllib.parse.quote(b["source"], safe="")
        dst = urllib.parse.quote(b["destination"], safe="")
        rk = b.get("routing_key", "")
        print(f"Binding: {b['source']} → {b['destination']} [routing_key={rk}]")
        api("POST", f"/bindings/{vhost}/e/{src}/q/{dst}", {
            "routing_key": rk,
            "arguments": b.get("arguments", {}),
        })
        counts["bindings"] += 1

    # Policies (PUT — overwrites if exists, which is the desired behavior)
    for p in topology.get("policies", []):
        name_enc = urllib.parse.quote(p["name"], safe="")
        print(f"Policy: {p['name']} (pattern: {p['pattern']})")
        api("PUT", f"/policies/{vhost}/{name_enc}", {
            "pattern": p["pattern"],
            "definition": p["definition"],
            "apply-to": p.get("apply_to", "queues"),
            "priority": p.get("priority", 0),
        })
        counts["policies"] += 1

    print(f"Topology provisioned: {counts}")

    return {
        "PhysicalResourceId": physical_id,
        "Data": {k: str(v) for k, v in counts.items()},
    }
