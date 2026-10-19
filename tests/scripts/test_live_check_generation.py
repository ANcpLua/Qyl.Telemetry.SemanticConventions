import importlib.util
from pathlib import Path
import re
import unittest


def load(name):
    path = Path(__file__).resolve().parents[2] / "scripts" / (name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


generator = load("generate-live-check")
future = load("check-future")


class LiveCheckGenerationTests(unittest.TestCase):
    def test_template_literals_are_escaped_without_restricting_operation_values(self):
        pattern = generator.name_pattern("agent {operation.name}", {}, {})
        self.assertIsNotNone(re.fullmatch(pattern, "agent a new operation"))
        self.assertIsNone(re.fullmatch(pattern, "some other producer"))
        pattern = generator.name_pattern("rpc.method", {}, {})
        self.assertIsNone(re.fullmatch(pattern, "rpcXmethod"))

    def test_a_free_form_name_does_not_claim_unrelated_spans(self):
        self.assertIsNone(generator.name_pattern("{db.query.summary}", {}, {}))

    def test_a_literal_prefix_still_routes_new_operation_values(self):
        attributes = {"operation": {"type": {"members": [{"value": "tool"}]}}}
        pattern = generator.name_pattern("agent {operation}", attributes, {})
        self.assertIsNotNone(re.fullmatch(pattern, "agent a new operation"))

    def test_enum_names_use_declared_wire_values_and_substitutions(self):
        attributes = {"method": {"type": {"members": [{"value": "GET"}, {"value": "_OTHER"}]}}}
        pattern = generator.name_pattern("{method} {path}", attributes, {"method": {"_OTHER": "HTTP"}})
        self.assertIsNotNone(re.fullmatch(pattern, "GET /checkout"))
        self.assertIsNotNone(re.fullmatch(pattern, "HTTP /checkout"))
        self.assertIsNone(re.fullmatch(pattern, "execute_tool /checkout"))

    def test_strict_check_rejects_owned_defects_and_unrecognized_dependency_errors(self):
        root = Path.cwd()
        def row(kind, path, **extra):
            return {"error": {"FailToResolveDefinition": {kind: {"path_or_url": path, **extra}}}}
        self.assertEqual("defect", future.classify(row("MissingRequirementLevelWarning", "registry/local.yaml"), root))
        self.assertEqual("dependency_warning", future.classify(row("MissingRequirementLevelWarning", "/external/model.yaml"), root))
        self.assertEqual("defect", future.classify(row("UnknownFutureError", "/external/model.yaml"), root))
        self.assertEqual("format_warning", future.classify(row("UnstableFileFormat", "registry/native.yaml", file_format="definition/2"), root))
        manifest_warning = {"error": {"FailToResolveDefinition": {"MissingManifestFileFormat": {"path": "/external/manifest.yaml"}}}}
        self.assertEqual("dependency_warning", future.classify(manifest_warning, root))

    def test_a_tool_failure_without_diagnostics_cannot_pass_the_gate(self):
        with self.assertRaises(ValueError):
            future.diagnostics("could not resolve the registry")


if __name__ == "__main__":
    unittest.main()
