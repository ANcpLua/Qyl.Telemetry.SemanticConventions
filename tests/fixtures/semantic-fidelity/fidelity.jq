def fidelity_context:
  {qyl_schema_url: .schema_url, core_schema_url: .schema_url,
     root: "fidelity", pascal_root: "Fidelity", type_root: "FidelityIncubating",
     tier: "incubating", namespace: "Fidelity", class_name: "FidelityViews",
     class_suffix: "", domains: ["app"],
     attributes: [.registry.attributes[] | select(.key | startswith("fidelity."))],
     metrics: .registry.metrics, spans: .refinements.spans,
     events: .registry.events, entities: [.registry.entities[] | select(.type | startswith("fidelity."))]};
