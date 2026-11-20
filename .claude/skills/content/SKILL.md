---
name: content
description: Add or change Stabby authored content (content/*.json such as biomes.json, sketch.json and their schemas). Use whenever a JSON file in content/ or its loader in Stabby.Content changes.
---

# Changing content

Content is data the generator reads, never code. Only `src/Stabby.Content`
opens files; `Stabby.World` receives typed objects.

Every change touches four places in one go:

1. **Data** - `content/<name>.json`. Keep `schemaVersion` at the top.
2. **Schema** - `content/schemas/<name>.schema.json`. Add every new field with
   type, range and `required`; keep `additionalProperties: false`. Bump the
   schema's version and the loader constant (e.g. `BiomeSchemaVersion`) when a
   field is renamed, removed or changes meaning.
3. **Loader** - `Stabby.Content` (`ContentLoader`, `SketchFile`). Map JSON to
   World types and reject bad data with a clear `InvalidDataException`
   naming the file and field. Range and cross-reference rules live in a World
   `Validate` method (e.g. `Biomes.Validate`) so Unity gets them too.
4. **Test** - extend the schema test (`BiomeContentMatchesSchema` pattern):
   every property in the data is allowed by the schema, required fields exist,
   counts match (`BiomeContentLoads`). New file = new schema test.

If the change alters generated worlds, follow the worldgen skill (version
bump, sweeps, sheet).

`sketch.json` round-trips: `gen` writes it, `--sketch` reads it back, and
`EditedSketchRoundTripsToSameWorld` must still pass. Keep new sketch fields
optional with defaults so older files still load.
