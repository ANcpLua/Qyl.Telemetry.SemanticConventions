package after_resolution

import rego.v1

# Content is optional even when the source's native log route records it by default.
# This checks the declared collection contract rather than changing vendor wire names.
deny contains violation(group.id, attribute.name, "Unbounded content must not be required.") if {
    some group in input.groups
    some attribute in group.attributes
    attribute.annotations.value.unbounded_size == true
    attribute.requirement_level == "required"
}

deny contains violation(group.id, attribute.name, "A qyl attribute phase must be start or end.") if {
    some group in input.groups
    some attribute in group.attributes
    phase := attribute.annotations.qyl.phase
    not phase in {"start", "end"}
}

deny contains violation(group.id, "-", "Operation state is additive: use updowncounter with unit 1.") if {
    some group in input.groups
    group.id == "metric.qyl.agent.operation.status"
    not valid_state_metric(group)
}

valid_state_metric(group) if {
    group.instrument == "updowncounter"
    group.unit == "1"
}

violation(group, attribute, description) := {
    "id": description,
    "type": "semconv_attribute",
    "category": "agent_observability",
    "group": group,
    "attr": attribute,
}
