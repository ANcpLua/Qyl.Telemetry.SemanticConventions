#!/usr/bin/env python3
"""Exercise the generated matchers through Weaver; requirements come from the registry."""
import json
import os
from pathlib import Path
import shlex
import subprocess
import tempfile


def sample_attribute(attribute):
    value_type = attribute["type"]
    if isinstance(value_type, dict):
        value = value_type["members"][0]["value"]
        value_type = "int" if isinstance(value, int) else "string"
    else:
        defaults = {"string": "example", "int": 1, "double": 1.0, "boolean": True, "any": "example"}
        value = defaults.get(value_type, [])
    return {"name": attribute["key"], "type": value_type, "value": value}


def findings(value):
    if isinstance(value, dict):
        for finding in value.get("all_advice", []):
            yield finding
        for key, child in value.items():
            if key != "all_advice":
                yield from findings(child)
    elif isinstance(value, list):
        for child in value:
            yield from findings(child)


def main():
    root = Path(__file__).resolve().parents[1]
    registry = json.loads((root / ".build/typescript-registry.json").read_text())["registry"]
    spans = {span["type"]: span for span in registry["spans"]}
    operation = spans["qyl.agent.operation.internal"]
    required = [a for a in operation["attributes"] if a["requirement_level"] == "required"]
    attributes = [sample_attribute(a) for a in required]
    operation_name = next(a["value"] for a in attributes if a["name"] == "qyl.agent.operation.name")
    samples = []
    for absent in [None] + [a["key"] for a in required]:
        samples.append({"name": "agent " + operation_name, "kind": "internal",
                        "resource": {"attributes": []},
                        "attributes": [a for a in attributes if a["name"] != absent]})
    for kind in ["client", "server"]:
        samples.append({"name": "anthropic.messages.create", "kind": kind, "attributes": []})
    with tempfile.TemporaryDirectory(prefix="qyl-signal-validation-") as directory:
        source = Path(directory) / "samples.json"
        source.write_text(json.dumps([{"span": sample} for sample in samples]))
        command = shlex.split(os.environ.get("WEAVER", "weaver"))
        run = subprocess.run(command + ["registry", "live-check", "-r", "registry", "--v2",
                             "--include-unreferenced", "--config", "registry/.weaver.toml",
                             "--advice-policies", "registry/policies/live_check_advice",
                             "--input-source", str(source), "--format", "json", "--no-stream", "--fail-on", "violation"],
                             cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output = root / "artifacts/logs/signal-validation.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(run.stdout)
    output.with_suffix(".stderr").write_text(run.stderr)
    if run.returncode != 1:
        raise AssertionError(f"Missing required fields must fail live-check (exit {run.returncode}): {run.stderr}")
    try:
        report = json.loads(run.stdout)
    except json.JSONDecodeError as error:
        raise AssertionError(f"Weaver produced no report: {run.stderr}") from error
    observed = [sample["span"] for sample in report["samples"]]
    if len(observed) != len(samples):
        raise AssertionError("Weaver did not validate every submitted sample")
    violations = [f for f in findings(observed[0]) if f["level"] == "violation"]
    if violations:
        raise AssertionError(f"The valid operation must pass without per-operation resource attributes: {violations}")
    for sample, absent in zip(observed, [None] + [a["key"] for a in required]):
        if sample["live_check_result"]["match_info"]["signal"] != operation["type"]:
            raise AssertionError("The operation must match even when a required attribute is missing")
        missing = {f["context"]["attribute_key"] for f in findings(sample)
                   if f["id"] == "required_attribute_not_present" and f["level"] == "violation"}
        if missing != ({absent} if absent else set()):
            raise AssertionError(f"Expected missing {absent}, found {missing}")
    for sample, kind in zip(observed[-2:], ["client", "server"]):
        if sample["live_check_result"]["match_info"]["signal"] != "anthropic.messages.create." + kind:
            raise AssertionError("A shared native name must retain its client/server contract")
    print("OK: valid operation accepted, each required field rejected when absent, native span kinds matched separately.")


if __name__ == "__main__":
    main()
