# Changelog

## 1.4.0

### What is new

- Typed signals, expressions and statements: `Signal.browser`, `Signal.server`, `Signal.rocket`, `Expr` and `Stmt`, and overloads of the `Ds` attributes that take them.
- Rocket helpers: props, component signals, template directives and a reader for the manifest that `publishRocketManifests` posts.
- `Ds.nonce` for Content Security Policy mode, `Ds.withCase` for the `__case` modifier, `Ds.bindProp` and `Ds.bindEvent`, `Ds.query`, `Ds.peek`, `Ds.setAllFiltered` and `Ds.toggleAllFiltered`.
- `RequestCancellation.Cleanup`, `OnEventModifier.Document`, and `PatchElementsOptions.ViewTransitionSelector` from SDK 1.4.0.
- Datastar 1.0.4 is the default script, and Rocket has its own script helper, `Ds.rocketCdnScript`.

### Upgrading

This section covers upgrading from version 1.3.0 or earlier. It lists what can stop your code compiling, what can produce warnings, what can clash with your own names, what changes the output without any compiler message, what changes in your dependencies, and what changes in Datastar itself.

#### Changes that stop your code compiling

**`OpenWhenHidden` is now `bool voption`.** The compiler reports error FS0001. Wrap the value in `ValueSome`:

```fsharp
// before
{ RequestOptions.Defaults with OpenWhenHidden = true }

// after
{ RequestOptions.Defaults with OpenWhenHidden = ValueSome true }
```

Take care with `OpenWhenHidden = false`. The old code never sent it, so `@post`, `@put`, `@patch` and `@delete` ignored it and kept running while the page was hidden, because that is Datastar's default for them.
`ValueSome false` is sent, so those requests are now cancelled when the page is hidden and started again when it is visible.
If you want the old behaviour, delete the line. On a `@get`, `ValueSome false` is the same as leaving it out.

#### Changes that produce warnings

Three types have a new case: `BackendAction.Query`, `RequestCancellation.Cleanup` and `OnEventModifier.Document`.
A `match` that lists every case of one of these types now gets warning FS0025, which is an error if your project treats warnings as errors.
Add a case for the new value, or a `_` case. Until you do, a value that reaches the `match` without a case raises `MatchFailureException`.

#### Name clashes

`open Falco.Datastar` now brings `Query`, `Cleanup` and `Document` into scope as union cases.
If your own code declares a union case with one of these names, and you open `Falco.Datastar` *after* that declaration, your name now means Falco's case. You get errors such as "expected to have type `Msg` but here has type `BackendAction`".
Either move `open Falco.Datastar` above your type, or put the type name in front of the case:

```fsharp
match msg with
| Msg.Query text -> text
| Msg.Cleanup -> "cleaned up"
```

A module or type of your own called `Rocket` does not clash.

#### Changes that compile but produce different output

**Text in expressions is escaped.** `Ds.get`, `Ds.post`, `Ds.put`, `Ds.patch`, `Ds.delete` and `Ds.query` now escape the URL, and `Ds.signal` escapes its value.
A URL such as `/items?a=1&b=2` is written with `&amp;`, which the browser reads as `&`. Before, a `'` in a URL or in a text value broke the expression, and text from a user could run code.
Tests that compare the generated text may need new expected values.

**`Ds.setAll` and `Ds.toggleAll`.** They used to write `@setAll('foo.', true)` and `@toggleAll('foo.')`. The Datastar actions take the value first and a filter second (`@setAll(value, filter)`) and only a filter (`@toggleAll(filter)`), so the old output did not do what it looked like.
They now write `@setAll(true, { include: /^foo\./ })` and `@toggleAll({ include: /^foo\./ })`. Your code needs no change, but tests that compare the generated text need new expected values.
Numbers are now written as numbers: `Ds.setAll ("foo.", 5)` used to write `'5'`, which is text, and now writes `5`. If you want text, pass a string.

**`RequestOptions.Retry`.** It used to be ignored, because the library never wrote it. `Retry = OnError`, `OnAlways` and `OnNever` now take effect, so if your code sets one of them, the retry behaviour of that request changes.

**`RequestOptions.RetryMaxWait`.** It was written as `retryMaxWaitMs`. Datastar stopped reading that name in 1.0.0 (RC.8 and earlier read it), so the setting was ignored. It is now written as `retryMaxWait`, which Datastar 1.0.4 reads.

**`ContentType = CustomJson obj`.** It used to send an option called `override`, which Datastar does not have, so Datastar ignored it and sent the signals as usual.
It now sends your object as the request body. If your server code expects the signals, change it to expect your object, or stop using `CustomJson`.

**`Ds.cdnSrc` and `Ds.cdnScript`.** They now load Datastar 1.0.4. They loaded 1.0.0-RC.7 before, so your pages move across several Datastar releases.
The changes in Datastar that can affect your pages are listed under [Changes in Datastar itself](#changes-in-datastar-itself), and the full list is in the [Datastar release notes](https://github.com/starfederation/datastar/releases).
To stay on the old script for now, write the tag yourself. Note that `Ds.query`, `RequestCancellation = Cleanup` and `RequestOptions.RetryMaxWait` need the newer script.

```fsharp
Elem.script [ Attr.type' "module"; Attr.src "https://cdn.jsdelivr.net/gh/starfederation/datastar@1.0.0-RC.7/bundles/datastar.js" ] []
```

#### Changes to your dependencies

`Falco.Datastar` now depends on `StarFederation.Datastar.FSharp` 1.4.0. It depended on 1.2.0. Your project picks up the new version by itself, and three things change.

Reading signals for a `@delete` now works. Datastar sends them in the query string, and versions 1.2.0 and 1.2.1 of the SDK only looked in the body, so `Request.getSignals` and `Request.getSignalsJson` came back empty for a `@delete`. If you worked around this by reading the query string yourself, you can remove the workaround.

If your own project references `StarFederation.Datastar.FSharp` directly at a version below 1.3.0, NuGet only warns (`NU1605`, "Detected package downgrade") and uses your older version, and the `@delete` problem comes back. Raise that reference to 1.4.0, or remove it.

`PatchElementsOptions` has a new field, `ViewTransitionSelector`, for scoped view transitions. `PatchElementsOptions.Defaults with ...` needs no change, but code that calls the constructor must pass the new argument.
Everything the SDK sends is otherwise the same as before.

#### Changes in Datastar itself

Moving from the RC.7 script to 1.0.4 changes how some pages behave, whatever your F# code does. These are the changes from the [release notes](https://github.com/starfederation/datastar/releases) that can affect a page built with this library.

**A `$name` inside quotes is no longer replaced (RC.8).** In an expression, `'Hello $name'` now stays as the text `Hello $name`. Join the parts instead, as in `'Hello ' + $name`, or use a template literal, where `${...}` is still replaced: `` `Hello ${$name}` ``. Since 1.0.4 the same holds for `@action(` inside a string. Both forms were checked in a browser.

**A request is no longer cancelled when its element is removed (RC.8).** `RequestCancellation.Auto` used to do that. Use `RequestCancellation = Cleanup` if you want it.

**Requests with the same method and URL cancel each other (1.0.2).** Starting a `@get('/items')` cancels an earlier `@get('/items')` that is still running, even when a different element started it. Use `RequestCancellation = Disabled` to let both run.

**A `@get` or `@delete` has no body and no `Content-Type` header (1.0.0).** Server code that looked for `application/json` on those requests will no longer find it. The signals are in the `datastar` query parameter.

**Checkboxes and radio buttons update the signal on `input` (1.0.2).** `Ds.bind` used to use `change` for them. Use `Ds.bindEvent` to choose the events yourself.

**A patch that changes an input's property fires `datastar-prop-change` and not `change` (1.0.0).** If you relied on the native `change` event after a server patch, listen for the new event with `Ds.onEvent ("datastar-prop-change", ...)`.

**A retried request sends the current signals (RC.8 and 1.0.3).** Before, a retry could send the values the signals had when the first attempt started.
