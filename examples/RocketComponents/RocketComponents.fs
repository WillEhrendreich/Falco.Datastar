open System
open Falco
open Falco.Datastar.SignalPath
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder

// Rocket components are written in JavaScript. The server renders their tags, props and children.
// `my-counter` uses shadow DOM. The server sends its props as attributes and changes them by patching the element.
// `my-toggle` uses light DOM. The server renders its children, which use the component's own signals and actions.
let components = $"""
import {{ rocket }} from '{Ds.rocketCdnSrc}'

rocket('my-counter', {{
  props: ({{ number, string }}) => ({{
    count: number.default(0),
    step: number.default(1),
    label: string.default('Count'),
  }}),
  render: ({{ html, props }}) => html`
    <p>
      <strong>${{props.label}}</strong>
      <button data-on:click="@post('/counter/change?by=${{-props.step}}')">-</button>
      <output>${{props.count}}</output>
      <button data-on:click="@post('/counter/change?by=${{props.step}}')">+</button>
    </p>
    <template data-if="${{props.count}} >= 10"><p>That is a lot.</p></template>
    <template data-else><p>Keep going.</p></template>
  `,
}})

rocket('my-toggle', {{
  mode: 'light',
  setup({{ $$, action }}) {{
    $$('on', false)
    $$('log', [])
    action('flip', ({{ state }}) => {{
      state.on = !state.on
      state.log = [...state.log, state.on ? 'on' : 'off']
    }})
  }},
}})
"""

let mutable count = 0

let counter =
    Elem.create "my-counter"
        [ Attr.id "counter"
          Rocket.propString ("label", "Clicks")
          Rocket.propNumber ("step", 1)
          Rocket.propNumber ("count", count) ]
        []

let toggleFrom (stamp:string) =
    Elem.create "my-toggle" [ Attr.id "toggle" ] [
        Elem.p [ Attr.id "stamp" ] [ Text.raw $"rendered by the server at {stamp}" ]
        Elem.button [ Attr.id "flip"; Ds.onClick (Rocket.call "flip") ] [ Text.raw "Toggle" ]
        Elem.p [ Attr.id "state"; Ds.text $"""{Rocket.local "on"} ? 'on' : 'off'""" ] []
        Elem.p [ Attr.id "shown"; Ds.show (Rocket.local "on") ] [ Text.raw "Now you see me." ]
        Elem.ul [ Attr.id "log" ] [
            Rocket.templateFor (Rocket.local "log", [ Elem.li [ Ds.text "n + ': ' + entry" ] [] ], item = "entry", index = "n")
        ]
        Elem.label [] [ Text.raw "Page signal, bound from inside the component: " ]
        Elem.input [ Attr.id "rooted"; Rocket.root (Ds.bind "query") ]
        Elem.label [] [ Text.raw " Component signal: " ]
        Elem.input [ Attr.id "scoped"; Ds.bind "note" ]
        Elem.p [ Attr.id "note" ] [ Text.raw "note: "; Elem.span [ Ds.text (Rocket.local "note") ] [] ]
    ]

let handleIndex : HttpHandler =
    let html =
        Elem.html [] [
            Elem.head [] [
                Elem.title [] [ Text.raw "Rocket Components" ]
                Ds.rocketCdnScript
                Elem.script [ Attr.type' "module" ] [ Text.raw components ]
            ]
            Elem.body [ Ds.signal (sp"query", "") ] [
                Text.h1 "Example: Rocket Components"
                Elem.h2 [] [ Text.raw "my-counter: props from the server" ]
                counter
                Elem.h2 [] [ Text.raw "my-toggle: children from the server" ]
                toggleFrom "first load"
                Elem.button [ Attr.id "patch"; Ds.onClick (Ds.post "/toggle/patch") ] [ Text.raw "Patch the toggle's children from the server" ]
                Elem.p [ Attr.id "query" ] [ Text.raw "page query: "; Elem.span [ Ds.text "$query" ] [] ]
            ]
        ]
    Response.ofHtml html

let handleChange : HttpHandler = fun ctx ->
    let by =
        match Int32.TryParse(string ctx.Request.Query["by"]) with
        | true, value -> value
        | _ -> 0
    count <- max 0 (count + by)
    let patched =
        Elem.create "my-counter"
            [ Attr.id "counter"
              Rocket.propString ("label", "Clicks")
              Rocket.propNumber ("step", 1)
              Rocket.propNumber ("count", count) ]
            []
    Response.ofHtmlElements patched ctx

let handlePatchToggle : HttpHandler = fun ctx ->
    Response.ofHtmlElements (toggleFrom (DateTime.Now.ToString "HH:mm:ss.fff")) ctx

let wapp = WebApplication.Create()

let endpoints =
    [ get "/" handleIndex
      post "/counter/change" handleChange
      post "/toggle/patch" handlePatchToggle ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
