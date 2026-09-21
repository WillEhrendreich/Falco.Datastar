# Unit tests

Run them with `dotnet test test/Falco.Datastar.Tests -c Release`. The browser tests are in `test/Falco.Datastar.E2E`.

## Simulation tests

Three files simulate the library's inputs and check rules that must always hold. They are deterministic: all of their randomness comes from a seed, so a failure can be replayed exactly. See `Dst.fs`.

| File | What it simulates | The rule it checks |
| --- | --- | --- |
| `DstHtmlTests.fs` | Hostile text (quotes, angle brackets, entities, line breaks, `__`, text that tries to add attributes) passed to every helper that writes an attribute or a template element | The call is refused with an `ArgumentException`, or a real HTML5 parser ([AngleSharp](https://github.com/AngleSharp/AngleSharp)) reads one element with exactly the attributes that were generated, and gives back the text they stand for |
| `DstManifestTests.fs`, the parser | Manifests that are changed in random places or damaged | `RocketManifest.parse` gives a result and never throws |
| `DstManifestTests.fs`, the stream | A request body that arrives in chunks of random sizes, fails partway, is cancelled partway, or is at the edge of the 1 MiB limit | `Request.getRocketManifests` gives the same result however the body arrives, never reads more than the limit and one chunk, and never gives a result for half a body |
| `EscapingTests.fs`, `ExprTests.fs`, `RequestOptionsTests.fs` | Random text, signal names and request options | The escaping is read back as the same text by a browser and a JavaScript parser, every accepted signal name is read by Datastar as written, and every set of options is valid JSON |

By default each test runs the seeds 1 to 25, so a normal run is fast and always the same. To look for rare cases, run more seeds. To replay a failure, run its seed:

```sh
DST_SEEDS=2000 dotnet test test/Falco.Datastar.Tests -c Release   # seeds 1 to 2000
DST_SEED=417 dotnet test test/Falco.Datastar.Tests -c Release     # only seed 417
```

A failure message names the seed and the command that replays it, and `DstHtmlTests` also prints the HTML that broke the rule.
The **dst** workflow in the Actions tab runs many seeds on GitHub. It only runs when you start it.

When a simulation finds a bug, fix it, and add a plain test for that case, so that the fix does not depend on a seed reaching it.
