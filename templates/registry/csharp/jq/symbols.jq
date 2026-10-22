# Shared wire-key -> C# symbol mapping, before stability or signal filtering.
def cs_pascal:
  gsub("[_* -]"; ".") | split(".")
  | map(select(length > 0) | (.[0:1] | ascii_upcase) + .[1:]) | join("");

def attribute_symbol_rows:
  ([.registry.attributes[], .dependencies[].registry.attributes[]] | unique_by(.key))
  | group_by(.key | cs_pascal)
  | map(sort_by([-(.key | split(".") | length), .key])
        | to_entries | map(.value + {
            cs_symbol: ((.value.key | cs_pascal) + (if .key == 0 then "" else "Native" end))
          })) | add
  | group_by(.cs_symbol)
  | map(if length > 1 then map(.cs_symbol += ("__" + (.key | explode | map(tostring) | join("_")))) else . end)
  | add
  | map(. + {
      cs_member: (if (.key | contains(".")) then .cs_symbol[(.key | split(".")[0] | cs_pascal | length):] else .cs_symbol end),
      cs_namespace: ("Qyl.Telemetry.SemanticConventions." +
        (if .stability == "stable" or .deprecated != null then "" else "Incubating." end) + "Activities"),
      cs_activity_class: ((.key | split(".")[0] | cs_pascal) +
        (if .stability == "stable" or .deprecated != null then "" else "Incubating" end) + "ActivityExtensions")
    });

def with_attribute_symbols:
  ([., .dependencies[]]) as $sources
  | ([$sources[] | (.registry.entities[]?, .refinements.entities[]?)
       | ((.identity // [])[], (.description // [])[]) | .key]
     | unique | map({key: ., value: true}) | from_entries) as $entity_keys
  | ([$sources[] | (.registry.spans[]?, .registry.events[]?, .registry.metrics[]?,
                    .refinements.spans[]?, .refinements.events[]?, .refinements.metrics[]?)
       | (.attributes // [])[] | .key]
     | unique | map({key: ., value: true}) | from_entries) as $signal_keys
  |
  (attribute_symbol_rows | map({key: .key, value: {
    cs_symbol: .cs_symbol, cs_member: .cs_member,
    cs_namespace: .cs_namespace, cs_activity_class: .cs_activity_class,
    cs_has_setter: (($entity_keys[.key] | not) or ($signal_keys[.key] // false))
  }}) | from_entries) as $symbols
  | walk(if type == "object" and (.key? | type) == "string" and $symbols[.key] != null
         then . + $symbols[.key] else . end);

# An attribute definition that carries `qyl.openai.literal_name` is written and read under that
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

