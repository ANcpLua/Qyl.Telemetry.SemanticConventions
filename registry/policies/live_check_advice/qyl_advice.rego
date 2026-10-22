package live_check_advice

import rego.v1

# The qyl live-check rules. Each one re-issues a finding Weaver's built-in advisors raise, at
# the level qyl's architecture actually implies, and each one decides that level from the
# registry entry alone -- there is no attribute-key list anywhere in this file.
#
# Weaver's built-in advisors are compiled into the binary and always run, and no policy can
# change the level they emit at. registry/.weaver.toml therefore drops `deprecated`,
# `type_mismatch` and `undefined_enum_variant` by finding id -- never by attribute name -- and
# these rules take their place. A filter drops by id whoever raised it, so every rule below
# carries an id of its own; the id names the case, and the level follows from the id:
#
#   (a) open_enum_value          information  an undocumented value on an enum whose members
#                                             include `_OTHER`. Such an enum is open: the
#                                             specification prescribes values outside the
#                                             member list, so this is not a defect.
#       undocumented_enum_value  information  an undocumented value on a closed enum. Weaver's
#                                             default level for the same case.
#   (b) deprecated_renamed       improvement  the collector rewrites the key to its final live
#                                             replacement (AttributeMapping.TryGetRename).
#       deprecated_obsoleted     improvement  the collector drops the key and counts it
#                                             (AttributeMapping.IsObsoleted).
#       deprecated_uncategorized violation    no replacement and no rule. Weaver's default.
#   (c) type_coercible           improvement  the value parses as the declared type. The
#                                             collector stores it with the type it arrived in;
#                                             a reader that needs the declared type converts it.
#       type_not_coercible       violation    it does not. Weaver's default level.
#       enum_type_invalid        violation    an enum sample that is neither string nor int.
#                                             Weaver's default level.
#
# Everything else keeps Weaver's default level, unchanged: `not_stable`, `missing_attribute`,
# the required/recommended/opt-in/conditionally-required attribute findings, `unit_mismatch`,
# `unexpected_instrument`, the entity association checks, and otel.rego's four name and
# namespace rules for undeclared names. Explicitly registered native spellings are accepted.

# ---------------------------------------------------------------------------
# (a) enum values
# ---------------------------------------------------------------------------

# Weaver's EnumAdvisor compares a sample value against the member list only for `string` and
# `int` samples; any other sample type is the TypeAdvisor's business.
enum_comparable_types := {"string", "int"}

enum_members := input.registry_attribute.type.members

documented_enum_value if {
	some member in enum_members
	member.value == input.sample.attribute.value
}

open_enum if {
	some member in enum_members
	member.value == "_OTHER"
}

undocumented_enum_value if {
	input.sample.attribute.value != null
	input.sample.attribute.type in enum_comparable_types
	count(enum_members) > 0
	not documented_enum_value
}

enum_context := {
	"attribute_key": input.sample.attribute.name,
	"attribute_value": input.sample.attribute.value,
}

deny contains make_advice("open_enum_value", "information", enum_context, message) if {
	undocumented_enum_value
	open_enum
	message := sprintf(
		"Enum attribute '%s' has value '%v' which is not documented. The enum is open -- it carries the member `_OTHER` -- so a value outside the member list is prescribed, not a defect.",
		[input.sample.attribute.name, input.sample.attribute.value],
	)
}

deny contains make_advice("undocumented_enum_value", "information", enum_context, message) if {
	undocumented_enum_value
	not open_enum
	message := sprintf(
		"Enum attribute '%s' has value '%v' which is not documented, and the enum is closed.",
		[input.sample.attribute.name, input.sample.attribute.value],
	)
}

# ---------------------------------------------------------------------------
# (b) deprecations
# ---------------------------------------------------------------------------

# Weaver's DeprecatedAdvisor covers attribute samples against the registry attribute, and
# metric and log samples against the registry signal group. All three are reproduced. The
# deprecation reaches rego as the registry declares it, so the rules read `reason` and
# `renamed_to` rather than matching on a key.

handled_deprecation_reasons := {"renamed", "obsoleted"}

deprecated_attribute := input.registry_attribute.deprecated

deprecated_signal := input.registry_group.deprecated

deprecation_context(name_key, name, deprecated) := {
	name_key: name,
	"deprecation_reason": object.get(deprecated, "reason", "uncategorized"),
	"deprecation_note": object.get(deprecated, "note", ""),
}

deny contains make_advice("deprecated_renamed", "improvement", context, message) if {
	input.sample.attribute
	deprecated_attribute.reason == "renamed"
	context := deprecation_context("attribute_key", input.sample.attribute.name, deprecated_attribute)
	message := sprintf(
		"Attribute '%s' is deprecated; reason = 'renamed', note = '%s'. The collector rewrites it to '%s'.",
		[input.sample.attribute.name, object.get(deprecated_attribute, "note", ""), deprecated_attribute.renamed_to],
	)
}

deny contains make_advice("deprecated_obsoleted", "improvement", context, message) if {
	input.sample.attribute
	deprecated_attribute.reason == "obsoleted"
	context := deprecation_context("attribute_key", input.sample.attribute.name, deprecated_attribute)
	message := sprintf(
		"Attribute '%s' is deprecated; reason = 'obsoleted', note = '%s'. It has no replacement; the collector drops it and counts it.",
		[input.sample.attribute.name, object.get(deprecated_attribute, "note", "")],
	)
}

deny contains make_advice("deprecated_uncategorized", "violation", context, message) if {
	input.sample.attribute
	deprecated_attribute
	not object.get(deprecated_attribute, "reason", "uncategorized") in handled_deprecation_reasons
	context := deprecation_context("attribute_key", input.sample.attribute.name, deprecated_attribute)
	message := sprintf(
		"Attribute '%s' is deprecated; reason = '%s', note = '%s'. The registry names no replacement, so the collector has no rule for it.",
		[input.sample.attribute.name, object.get(deprecated_attribute, "reason", "uncategorized"), object.get(deprecated_attribute, "note", "")],
	)
}

deny contains make_advice("deprecated_renamed", "improvement", context, message) if {
	input.sample.metric
	deprecated_signal.reason == "renamed"
	context := deprecation_context("metric_name", input.sample.metric.name, deprecated_signal)
	message := sprintf(
		"Metric '%s' is deprecated; reason = 'renamed', note = '%s'. It was renamed to '%s'.",
		[input.sample.metric.name, object.get(deprecated_signal, "note", ""), deprecated_signal.renamed_to],
	)
}

deny contains make_advice("deprecated_obsoleted", "improvement", context, message) if {
	input.sample.metric
	deprecated_signal.reason == "obsoleted"
	context := deprecation_context("metric_name", input.sample.metric.name, deprecated_signal)
	message := sprintf(
		"Metric '%s' is deprecated; reason = 'obsoleted', note = '%s'. It has no replacement.",
		[input.sample.metric.name, object.get(deprecated_signal, "note", "")],
	)
}

deny contains make_advice("deprecated_uncategorized", "violation", context, message) if {
	input.sample.metric
	deprecated_signal
	not object.get(deprecated_signal, "reason", "uncategorized") in handled_deprecation_reasons
	context := deprecation_context("metric_name", input.sample.metric.name, deprecated_signal)
	message := sprintf(
		"Metric '%s' is deprecated; reason = '%s', note = '%s'. The registry names no replacement.",
		[input.sample.metric.name, object.get(deprecated_signal, "reason", "uncategorized"), object.get(deprecated_signal, "note", "")],
	)
}

deny contains make_advice("deprecated_renamed", "improvement", context, message) if {
	input.sample.log
	deprecated_signal.reason == "renamed"
	context := deprecation_context("event_name", input.sample.log.event_name, deprecated_signal)
	message := sprintf(
		"Event '%s' is deprecated; reason = 'renamed', note = '%s'. It was renamed to '%s'.",
		[input.sample.log.event_name, object.get(deprecated_signal, "note", ""), deprecated_signal.renamed_to],
	)
}

deny contains make_advice("deprecated_obsoleted", "improvement", context, message) if {
	input.sample.log
	deprecated_signal.reason == "obsoleted"
	context := deprecation_context("event_name", input.sample.log.event_name, deprecated_signal)
	message := sprintf(
		"Event '%s' is deprecated; reason = 'obsoleted', note = '%s'. It has no replacement.",
		[input.sample.log.event_name, object.get(deprecated_signal, "note", "")],
	)
}

deny contains make_advice("deprecated_uncategorized", "violation", context, message) if {
	input.sample.log
	deprecated_signal
	not object.get(deprecated_signal, "reason", "uncategorized") in handled_deprecation_reasons
	context := deprecation_context("event_name", input.sample.log.event_name, deprecated_signal)
	message := sprintf(
		"Event '%s' is deprecated; reason = '%s', note = '%s'. The registry names no replacement.",
		[input.sample.log.event_name, object.get(deprecated_signal, "reason", "uncategorized"), object.get(deprecated_signal, "note", "")],
	)
}

# ---------------------------------------------------------------------------
# (c) type mismatches
# ---------------------------------------------------------------------------

# Weaver's TypeAdvisor raises `type_mismatch` on an attribute sample in two shapes. Both are
# reproduced; the second is where the qyl rule differs.

# A wire key can belong to more than one registry definition. openai_codex.thread.id travels as
# thread.id, the key of the core integer managed thread id, and carries the Codex conversation
# thread UUID as a string. Its signal references name the wire key in `qyl.openai.literal_name`.
# When the live-check matchers tied the sample to a signal (`input.registry_group`) whose
# reference carries the sample's key that way, the reference's type is the declared one; else the
# definition that owns the key (`input.registry_attribute`) declares it.
wire_reference_type := reference.type if {
	some reference in input.registry_group.attributes
	reference.annotations.qyl.openai.literal_name == input.sample.attribute.name
}

registry_type := wire_reference_type

registry_type := input.registry_attribute.type if not wire_reference_type

# The declared type, with `template[...]` unwrapped to the type it templates.
declared_type := registry_type if {
	is_string(registry_type)
	not startswith(registry_type, "template[")
}

declared_type := trim_suffix(trim_prefix(registry_type, "template["), "]") if {
	is_string(registry_type)
	startswith(registry_type, "template[")
}

# PrimitiveOrArrayTypeSpec::is_compatible: `any` matches anything in either direction, and an
# observed int is accepted where a double is declared, because OTLP serializers emit
# int_value for integral numbers.
compatible_type(observed, declared) if observed == declared

compatible_type(observed, _) if observed == "any"

compatible_type(_, declared) if declared == "any"

compatible_type(observed, declared) if {
	observed == "int"
	declared == "double"
}

compatible_type(observed, declared) if {
	observed == "int[]"
	declared == "double[]"
}

type_mismatch if {
	input.sample.attribute.type
	declared_type
	not compatible_type(input.sample.attribute.type, declared_type)
}

# The value parses as the declared type, so a reader converting it loses nothing.
parses_as_declared if {
	declared_type == "int"
	regex.match(`^\s*[-+]?[0-9]+\s*$`, sprintf("%v", [input.sample.attribute.value]))
}

parses_as_declared if {
	declared_type == "double"
	regex.match(`^\s*[-+]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][-+]?[0-9]+)?\s*$`, sprintf("%v", [input.sample.attribute.value]))
}

parses_as_declared if {
	declared_type == "boolean"
	lower(sprintf("%v", [input.sample.attribute.value])) in {"true", "false"}
}

# Every scalar has a string form, so a scalar observed where a string is declared converts.
parses_as_declared if {
	declared_type == "string"
	input.sample.attribute.type in {"int", "double", "boolean"}
}

type_context := {
	"attribute_key": input.sample.attribute.name,
	"attribute_type": input.sample.attribute.type,
}

# Shape 1: the registry declares an enum and the sample is neither string nor int. An enum
# value can only be one of those two, so there is nothing to coerce.
deny contains make_advice("enum_type_invalid", "violation", type_context, message) if {
	input.sample.attribute.type
	count(enum_members) > 0
	not input.sample.attribute.type in enum_comparable_types
	message := sprintf(
		"Enum attribute '%s' has type '%s'. Enum value type should be 'string' or 'int'.",
		[input.sample.attribute.name, input.sample.attribute.type],
	)
}

# Shape 2: the observed primitive type is incompatible with the declared one.
deny contains make_advice("type_coercible", "improvement", context, message) if {
	type_mismatch
	parses_as_declared
	context := object.union(type_context, {"expected": declared_type})
	message := sprintf(
		"Attribute '%s' has type '%s'. Type should be '%s'. The value '%v' parses as '%s'; the collector stores it with the type it arrived in.",
		[input.sample.attribute.name, input.sample.attribute.type, declared_type, input.sample.attribute.value, declared_type],
	)
}

deny contains make_advice("type_not_coercible", "violation", context, message) if {
	type_mismatch
	not parses_as_declared
	context := object.union(type_context, {"expected": declared_type})
	message := sprintf(
		"Attribute '%s' has type '%s'. Type should be '%s', and the value '%v' does not parse as one.",
		[input.sample.attribute.name, input.sample.attribute.type, declared_type, input.sample.attribute.value],
	)
}

# ---------------------------------------------------------------------------

# `make_advice` is not redefined here. otel.rego, in this same package and this same
# directory, already declares it, and a second definition of the same function in the same
# package makes Weaver's rego engine abort the run with "loop hoisting table out of bounds".
# The two files ship together, so one definition is enough.
