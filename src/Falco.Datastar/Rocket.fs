namespace Falco.Datastar

open System
open System.Globalization
open System.Text.Json
open Falco.Markup

/// <summary>
/// Server-side helpers for Rocket, Datastar's web-component layer (load it with <see cref="Ds.rocketCdnScript"/>).
/// Components are defined in JavaScript with <c>rocket(tag, definition)</c>; these helpers render the server's side of the contract:
/// the props a component reads from its attributes, the local signals and actions its children may use, and its template directives.
/// Nothing here sets a value the caller did not pass, so Rocket's own defaults apply.
/// https://github.com/starfederation/datastar/tree/v1.0.4/library/src/rocket
/// </summary>
[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Rocket =
    /// <summary>
    /// A signal that is private to one component instance, for use in expressions inside the component, e.g. <c>Ds.text (Rocket.local "count")</c>.
    /// Rocket rewrites it to a path unique to the instance, so two instances of a component do not share it.
    /// </summary>
    /// <param name="name">The local signal, declared in the component's setup with <c>$$('name', initialValue)</c></param>
    /// <returns>Expression</returns>
    static member local (name:string) =
        "$$" + name

    /// <summary>
    /// Calls an action of the component the expression is inside, e.g. <c>Ds.onClick (Rocket.call "flip")</c>.
    /// Rocket looks for a component action with that name and falls back to Datastar's global actions.
    /// </summary>
    /// <param name="name">The action, registered in the component's setup with <c>action('name', fn)</c></param>
    /// <param name="args">Expressions passed to the action</param>
    /// <returns>Expression</returns>
    static member call (name:string, ?args:string list) =
        let arguments = defaultArg args [] |> String.concat ", "
        $"@{name}({arguments})"

    /// <summary>
    /// Makes a bind, computed, indicator or ref attribute refer to the page's signal instead of the component's own.
    /// Inside a component Rocket scopes these attributes to the instance; this opts one out (Rocket's <c>__root</c> modifier).
    /// It does not apply to <c>data-signals</c>, which Rocket always scopes.
    /// </summary>
    /// <param name="attribute">The attribute to opt out, e.g. <c>Ds.bind "query"</c></param>
    /// <returns>Attribute</returns>
    static member root (attribute:XmlAttribute) =
        match attribute with
        | KeyValueAttr (key, value) -> KeyValueAttr (key + "__root", value)
        | NonValueAttr key -> NonValueAttr (key + "__root")

    static member private prop (name:string, encoded:string) =
        Attr.create (String.datastarKebab name) (Js.attrEncode encoded)

    /// <summary>
    /// A string prop. The attribute name is the prop name the way Rocket derives it, e.g. <c>maxCount</c> becomes <c>max-count</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component, e.g. "label"</param>
    /// <param name="value">The value; it is escaped for the attribute</param>
    /// <returns>Attribute</returns>
    static member propString (name:string, value:string) =
        Rocket.prop (name, value)

    /// <summary>
    /// A number prop. Written with the invariant culture, because the browser parses with a dot whatever the server's culture is.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">Any numeric type</param>
    /// <returns>Attribute</returns>
    static member propNumber<'T when 'T :> IFormattable> (name:string, value:'T) =
        Rocket.prop (name, (value :> IFormattable).ToString(null, CultureInfo.InvariantCulture))

    /// <summary>
    /// A boolean prop. Always written, as "true" or "false": leaving the attribute out means the prop's default, which may be true.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The value</param>
    /// <returns>Attribute</returns>
    static member propBool (name:string, value:bool) =
        Rocket.prop (name, Bool.eitherOr "true" "false" value)

    /// <summary>
    /// A date prop, written as UTC ISO 8601 with milliseconds, the same as JavaScript's <c>Date.toISOString()</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The moment; it is converted to UTC</param>
    /// <returns>Attribute</returns>
    static member propDate (name:string, value:DateTimeOffset) =
        Rocket.prop (name, value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))

    /// <summary>
    /// A structured prop, written as JSON: for Rocket's json, array, tuple, object and oneOf codecs.
    /// Property names are camelCase, like the JavaScript objects the component decodes them into, unless you pass options.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">Serialized with System.Text.Json; it is escaped for the attribute</param>
    /// <param name="options">Optional options for the JSON serializer</param>
    /// <returns>Attribute</returns>
    static member propJson<'T> (name:string, value:'T, ?options:JsonSerializerOptions) =
        Rocket.prop (name, JsonSerializer.Serialize<'T>(value, defaultArg options Js.webJsonOptions))

    /// <summary>
    /// A binary prop, written as base64, which the component's bin codec decodes with <c>atob</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The bytes</param>
    /// <returns>Attribute</returns>
    static member propBin (name:string, value:byte[]) =
        Rocket.prop (name, Convert.ToBase64String value)

    /// <summary>
    /// Repeats the children for each item of a signal or expression, e.g. <c>Rocket.templateFor (Rocket.local "todos", [ ... ], item = "todo")</c>.
    /// Rocket names the item <c>item</c> and the index <c>i</c> unless told otherwise; those are left alone unless you pass them.
    /// The attribute is always <c>data-for</c>: Rocket does not honour a custom attribute prefix for its template directives.
    /// </summary>
    /// <param name="source">An expression that evaluates to an array, iterable or string</param>
    /// <param name="children">The row, which can use the item and the index by name</param>
    /// <param name="item">Names the item; when only <paramref name="index"/> is given the item is called <c>item</c></param>
    /// <param name="index">Names the index</param>
    /// <returns>Element</returns>
    static member templateFor (source:string, children:XmlNode list, ?item:string, ?index:string) =
        let expression =
            match item, index with
            | None, None -> source
            | Some item', None -> $"{item'} in {source}"
            | None, Some index' -> $"item, {index'} in {source}"
            | Some item', Some index' -> $"{item'}, {index'} in {source}"
        Elem.template [ Attr.create "data-for" (Js.attrEncode expression) ] children

    /// <summary>
    /// Renders the children only while the condition is true. Follow it with <see cref="templateElseIf"/> and <see cref="templateElse"/> siblings for a chain.
    /// The attribute is always <c>data-if</c>: Rocket does not honour a custom attribute prefix for its template directives.
    /// </summary>
    /// <param name="condition">An expression</param>
    /// <param name="children">What to render</param>
    /// <returns>Element</returns>
    static member templateIf (condition:string, children:XmlNode list) =
        Elem.template [ Attr.create "data-if" (Js.attrEncode condition) ] children

    /// <summary>
    /// The next branch of a chain that starts with <see cref="templateIf"/>; it must directly follow a templateIf or another templateElseIf.
    /// </summary>
    /// <param name="condition">An expression</param>
    /// <param name="children">What to render</param>
    /// <returns>Element</returns>
    static member templateElseIf (condition:string, children:XmlNode list) =
        Elem.template [ Attr.create "data-else-if" (Js.attrEncode condition) ] children

    /// <summary>
    /// The last branch of a chain; it must directly follow a templateIf or a templateElseIf.
    /// </summary>
    /// <param name="children">What to render</param>
    /// <returns>Element</returns>
    static member templateElse (children:XmlNode list) =
        Elem.template [ Attr.createBool "data-else" ] children
