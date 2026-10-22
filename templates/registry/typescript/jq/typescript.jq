# The registry -> @ancplua/qyl-semantic-conventions mapping: OpenTelemetry JavaScript constant
# names, the two export tiers and the catalog rows. Every value leaves as a JavaScript literal
# (jq's tojson, not MiniJinja's, which escapes ' < > & for HTML).

# A wire key, metric, event or member id as a constant segment: upper-cased, every run of other
# characters one underscore, none leading or trailing.
def ts_symbol: ascii_upcase | gsub("[^A-Z0-9]+"; "_") | gsub("^_|_$"; "");

# The csharp target's with_wire_keys (templates/registry/csharp/jq/symbols.jq), so both packages
# key an attribute the same way. An attribute definition that carries `qyl.openai.literal_name` is written and read under that
# wire name, not under its registry id: openai_codex.thread.id arrives as thread.id. Every
# reference to it takes the wire name, whatever annotations the reference itself declares. Where
# the wire name is another definition's key, that definition keeps the key in every per-key
# catalog and the literal-name definition moves to `context_dependent_attributes`: the type
# under such a key depends on the signal that carries it.
def with_wire_keys:
  ([.registry.attributes[], .dependencies[].registry.attributes[]]) as $definitions
  | ($definitions
     | map(select((.annotations.qyl.openai.literal_name? | type) == "string"))
     | map({key: .key, value: .annotations.qyl.openai.literal_name}) | from_entries) as $wire
  | ($definitions | map(select($wire[.key] == null) | {key: .key, value: true}) | from_entries) as $owned
  | def shadowed: $wire[.key] != null and $owned[$wire[.key]] == true;
  ([.registry.attributes[] | select(shadowed) | . + {registry_id: .key, key: $wire[.key]}]) as $context_dependent
  | .registry.attributes |= map(select(shadowed | not))
  | walk(if type == "object" and (.key? | type) == "string" and $wire[.key] != null
         then . + {registry_id: .key, key: $wire[.key]}
         elif type == "object" and (.attributes? | type) == "array"
              and any(.attributes[]; (.key? | type) == "string" and $wire[.key] != null)
         then .attributes |= (map(if $wire[.key] != null then . + {registry_id: .key, key: $wire[.key]} else . end)
                              | sort_by(.key))
         else . end)
  | .context_dependent_attributes = $context_dependent;

# The rows of one kind by $key: the first dependency that defines a key, then the qyl registry's
# own rows over it, sorted by key.
def ts_rows($kind; $key):
  . as $registry
  | reduce ($registry.dependencies[].registry[$kind][]?) as $row ({};
      if has($row[$key]) then . else .[$row[$key]] = $row end)
  | reduce ($registry.registry[$kind][]?) as $row (.; .[$row[$key]] = $row)
  | [.[]] | sort_by(.[$key]);

# The root export carries stable and deprecated rows, ./incubating every row; an enum member
# without its own stability has its attribute's. code_generation.exclude keeps a row out of both.
def ts_included($tier):
  ((.annotations.code_generation.exclude // false) | not)
  and ($tier == "incubating" or .stability == "stable" or .deprecated != null);

def ts_member_included($tier; $attribute):
  ((.annotations.code_generation.exclude // false) | not)
  and ($tier == "incubating"
       or (if has("stability") then .stability else $attribute.stability end) == "stable"
       or .deprecated != null);

# Wire key -> constant segment for one tier's attributes. Two spellings of one dotted key
# (session.id and session_id) keep the dotted one plain and suffix the native one _NATIVE; any
# other collision fails generation.
def ts_attribute_symbols:
  [ group_by(.key | ts_symbol)[]
    | (.[0].key | ts_symbol) as $symbol
    | map(.key) as $keys
    | ($keys | map(split(".") | length) | max) as $segments
    | ($keys | map(select(split(".") | length == $segments))) as $dotted
    | if ($keys | length) > 1
         and (($keys | length) != 2 or ($dotted | length) != 1
              or ($keys | map(gsub("[_-]"; ".")) | unique | length) != 1)
      then error("attribute identifier collision: \($keys)") else . end
    | $keys[]
    | {key: ., value: (if . == $dotted[0] then $symbol else $symbol + "_NATIVE" end)} ]
  | if (map(.value) | unique | length) != length
    then error("attribute identifier collision: \([group_by(.value)[] | select(length > 1) | map(.key)][0])")
    else from_entries end;

def ts_definition_type:
  "{ readonly name: \(.name | tojson); readonly unit: \(.unit | tojson); readonly instrument: \(.instrument | tojson); readonly stability: \(.stability | tojson); }";

# One tier's exports, sorted by name: SCHEMA_URL, REGISTRY_SNAPSHOT_SHA256, ATTR_*, enum
# *_VALUE_*, METRIC_* with *_UNIT and *_DEFINITION, and EVENT_*. A name bound to two different
# values fails generation. value is the JavaScript literal, type its declared type.
def ts_exports($tier; $digest):
  . as $registry
  | [ts_rows("attributes"; "key")[] | select(ts_included($tier))] as $attributes
  | ($attributes | ts_attribute_symbols) as $symbols
  | [ {name: "SCHEMA_URL", value: $registry.schema_url},
      {name: "REGISTRY_SNAPSHOT_SHA256", value: $digest},
      ( $attributes[]
        | . as $attribute
        | {name: ("ATTR_" + $symbols[.key]), value: .key},
          ( (if (.type | type) == "object" then .type.members[]? else empty end)
            | select(ts_member_included($tier; $attribute))
            | {name: ($symbols[$attribute.key] + "_VALUE_" + (.id | ts_symbol)), value: .value} ) ),
      ( $registry | ts_rows("metrics"; "name")[] | select(ts_included($tier))
        | ("METRIC_" + (.name | ts_symbol)) as $metric
        | (if has("unit") then .unit else "" end) as $unit
        | {name: $metric, value: .name},
          {name: ($metric + "_UNIT"), value: $unit},
          {name: ($metric + "_DEFINITION"),
           value: {name: .name, unit: $unit, instrument: .instrument,
                   stability: (if has("stability") then .stability else "development" end)}} ),
      ( $registry | ts_rows("events"; "name")[] | select(ts_included($tier))
        | {name: ("EVENT_" + (.name | ts_symbol)), value: .name} ) ]
  | [ group_by(.name)[]
      | if (map(.value) | unique | length) > 1
        then error("identifier collision: \(.[0].name)") else .[0] end
      | {name,
         value: (.value | tojson),
         type: (if (.value | type) == "object" then .value | ts_definition_type else .value | tojson end)} ];

# The catalog rows: the rows and value kinds of the C# registry facts (registry_facts in
# templates/registry/csharp/weaver.yaml), every field a JavaScript literal.
def ts_value_kind:
  if type == "object" then
    {kind: (if (.members[0].value | type) == "number" then "Integer" else "String" end), template: false}
  elif type != "string" then {kind: "Unknown", template: false}
  else (if startswith("template[") then {p: .[9:-1], template: true} else {p: ., template: false} end)
       | {kind: ({"string": "String", "int": "Integer", "double": "Double", "boolean": "Boolean",
                  "string[]": "StringArray", "int[]": "IntegerArray", "double[]": "DoubleArray",
                  "boolean[]": "BooleanArray", "any": "Any"}[.p] // "Unknown"),
          template: .template}
  end;

def ts_catalog:
  ([.registry.attributes[]]) as $top
  | ($top | map({key: .key, value: true}) | from_entries) as $seen
  | ([.dependencies[].registry.attributes[]] | map(select($seen[.key] | not))) as $extra
  | ([.registry.metrics[]]) as $metric_top
  | ($metric_top | map({key: .name, value: true}) | from_entries) as $metric_seen
  | ([.dependencies[].registry.metrics[]] | map(select($metric_seen[.name] | not))) as $metric_extra
  | {attributes: [($top + $extra) | sort_by(.key)[]
                  | (.type | ts_value_kind) as $kind
                  | {name: (.key | tojson), valueKind: ($kind.kind | tojson),
                     isTemplate: ($kind.template | tojson),
                     stability: ((.stability // "development") | tojson),
                     deprecated: (.deprecated | tojson)}],
     metrics: [($metric_top + $metric_extra) | sort_by(.name)[]
               | {name: (.name | tojson), instrument: (.instrument | tojson), unit: (.unit | tojson),
                  stability: ((.stability // "development") | tojson),
                  deprecated: (.deprecated | tojson)}]};

# A string list as the policy module spells it, a space after each comma.
def ts_list: "[" + (map(tojson) | join(", ")) + "]";

# Each module once per file: the TypeScript source, its JavaScript and its declarations.
def ts_files($module):
  {flavor: "ts", file: "src/\($module).ts"},
  {flavor: "js", file: "dist/\($module).js"},
  {flavor: "d.ts", file: "dist/\($module).d.ts"};
