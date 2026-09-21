namespace Falco.Datastar

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Web
open Falco.Markup
open StarFederation.Datastar.FSharp

module Constants =
    let mutable dataSlugPrefix = "data"

type SignalsFilter =
    { IncludePattern : string voption
      ExcludePattern : string voption }
    static member None = { IncludePattern = ValueNone; ExcludePattern = ValueNone }
    static member Include pattern =  { IncludePattern = ValueSome pattern; ExcludePattern = ValueNone }
    static member Exclude pattern =  { IncludePattern = ValueNone; ExcludePattern = ValueSome pattern }
    /// Includes every signal whose path starts with the prefix, e.g. "form." matches "form.name" but not "formal"
    static member Prefix (prefix:string) =
        SignalsFilter.Include ("^" + Regex.Escape(prefix).Replace("/", "\\/"))
    static member Serialize (signalFilter:SignalsFilter) =
        if signalFilter = SignalsFilter.None then
            ""
        else
            StringBuilder()
            |> _.Append("{ ")
            |> (fun sb ->
                let filters = seq {
                    if (signalFilter.IncludePattern <> ValueNone) then
                        signalFilter.IncludePattern |> ValueOption.get |> (fun incStr -> $"include: /{incStr}/")
                    if (signalFilter.ExcludePattern <> ValueNone) then
                        signalFilter.ExcludePattern |> ValueOption.get |> (fun excStr -> $"exclude: /{excStr}/")
                    }
                sb.AppendJoin(',', filters)
                )
            |> _.Append(" }")
            |> _.ToString()

module SignalsFilter =
    let sf (includePattern:string) = SignalsFilter.Include includePattern

module SignalPath =
    let sp = SignalPath.create

    let getSignalFromJson<'T> (signalPath:SignalPath) (jsonDocument:JsonDocument) =
        let getSignalCore (jsonElement:JsonElement) (signalPath:SignalPath) =
            signalPath
            |> SignalPath.keys
            |> Seq.fold (fun (currentJsonElementOpt:JsonElement voption) (key:string) ->
                currentJsonElementOpt
                |> ValueOption.bind (fun (jsonElement:JsonElement) ->
                    match jsonElement.TryGetProperty(key) with
                    | false, _ -> ValueNone
                    | true, jsonElement -> ValueSome jsonElement
                    )
                ) (ValueSome jsonElement)
        try
            getSignalCore jsonDocument.RootElement signalPath |> ValueOption.map _.Deserialize<'T>()
        with | _ -> ValueNone

    let createJsonNodeFromPathAndValue<'T> signalPath (signalValue:'T) =
        signalPath
        |> SignalPath.keys
        |> Seq.rev
        |> Seq.fold (fun json key ->
            JsonObject([ KeyValuePair<string, JsonNode> (key, json) ]) :> JsonNode
            ) (JsonValue.Create(signalValue) :> JsonNode)

module Selector =
    let sel = Selector.create

type IntersectsVisibility =
    /// Triggers on exit
    | Exit
    /// Triggers when half of the element is visible
    | Half
    /// Triggers when the full element is visible
    | Full

type BackendAction =
    | Get of url:string
    | Post of url:string
    | Put of url:string
    | Patch of url:string
    | Delete of url:string
    /// The HTTP QUERY method. Like GET, it is safe to repeat, but it can carry a body
    | Query of url:string

type ContentType =
    /// default, filtered signals; default
    | Json
    /// sends a custom object as the request payload instead of the default, filtered signals
    | CustomJson of obj
    /// validates inputs of closest form and sends them to the backend
    | Form
    /// similar to Form, but specify the form id to send
    | SelectedForm of StarFederation.Datastar.FSharp.Selector

type Retry =
    /// retry on network errors; default
    | OnAuto
    /// retries on 4xx and 5xx responses
    | OnError
    /// retries on all non-204 responses, except redirects
    | OnAlways
    /// disables retry
    | OnNever
    with
    static member Serialize (retry:Retry) =
        match retry with
        | OnAuto -> "auto"
        | OnError -> "error"
        | OnAlways -> "always"
        | OnNever -> "never"

type RequestCancellation =
    /// cancels an earlier request with the same method and URL; default
    | Auto
    /// allows concurrent requests
    | Disabled
    /// like Auto, but also cancels the request when the element it is on is removed from the DOM
    | Cleanup
    /// an object name that can be aborted; https://data-star.dev/reference/actions#request-cancellation;
    /// creator should include '$', e.g. (AbortController "$controller")
    | AbortController of string
    with
    static member Serialize (requestCancellation:RequestCancellation) =
        match requestCancellation with
        | Auto -> "auto"
        | Disabled -> "disabled"
        | Cleanup -> "cleanup"
        | AbortController controller -> controller

type ResponseOverrideMode =
    | Outer
    | Inner
    | Remove
    | Replace
    | Prepend
    | Append
    | Before
    | After

/// Request Options for backend action plugins
/// https://data-star.dev/reference/action_plugins
type RequestOptions = {
      /// The type of content to send. A value of json sends all signals in a JSON request.
      /// A value of form tells the action to look for the closest form to the element on which it is placed
      /// (unless a selector option is provided), perform validation on the form elements,
      /// and send them to the backend using a form request (no signals are sent). Defaults to json.
      ContentType: ContentType

      /// Filter object utilizing regular expressions for which signals to send
      FilterSignals: SignalsFilter

      /// HTTP Headers to send with the request.
      Headers: (string * string) list

      /// Whether to keep the connection open when the page is hidden. Useful for dashboards
      /// but can cause a drain on battery life and other resources when enabled.
      /// Not set by default, so Datastar chooses: false for @get and true for the other actions.
      OpenWhenHidden: bool voption

      /// Determines on what to retry; auto, error, always, never
      Retry: Retry

      /// The retry interval in milliseconds. Defaults to 1 second
      RetryInterval: TimeSpan

      /// A numeric multiplier applied to scale retry wait times. Defaults to 2.
      RetryScaler: float

      /// The maximum allowable wait time in milliseconds between retries. Defaults to 30 seconds.
      RetryMaxWait: TimeSpan

      /// The maximum number of retry attempts. Defaults to 10.
      RetryMaxCount: int

      /// An AbortSignal object that can be used to cancel the request.
      /// https://data-star.dev/reference/actions#request-cancellation
      RequestCancellation: RequestCancellation
      }
    with
    static member Defaults =
        { ContentType = Json
          FilterSignals = SignalsFilter.None
          Headers = []
          OpenWhenHidden = ValueNone
          Retry = Retry.OnAuto
          RetryInterval = TimeSpan.FromSeconds(1.0)
          RetryScaler = 2.0
          RetryMaxWait = TimeSpan.FromSeconds(30.0)
          RetryMaxCount = 10
          RequestCancellation = Auto }

    static member inline With contentType = { RequestOptions.Defaults with ContentType = contentType }

    static member internal Serialize (backendActionOptions:RequestOptions) =
        let jsonObject = JsonObject()

        match backendActionOptions.ContentType with
        | _ when backendActionOptions.ContentType = RequestOptions.Defaults.ContentType -> ()
        | Form -> jsonObject.Add("contentType", "form")
        | SelectedForm formSelector ->
            jsonObject.Add("contentType", "form")
            jsonObject.Add("selector", formSelector)
        | CustomJson customJson ->
            jsonObject.Add("contentType", "json")
            jsonObject.Add("payload", JsonSerializer.SerializeToNode(customJson, JsonSerializerOptions.SignalsDefault))
        | Json -> jsonObject.Add("contentType", "json")

        if backendActionOptions.FilterSignals <> RequestOptions.Defaults.FilterSignals then
            jsonObject.Add("filterSignals", backendActionOptions.FilterSignals |> SignalsFilter.Serialize |> JsonNode.Parse)

        if backendActionOptions.Headers.Length > 0 then
            let headerObject = JsonObject()
            backendActionOptions.Headers |> List.iter headerObject.Add
            jsonObject.Add("headers", headerObject)

        backendActionOptions.OpenWhenHidden
        |> ValueOption.iter (fun openWhenHidden -> jsonObject.Add("openWhenHidden", JsonValue.Create openWhenHidden))

        if backendActionOptions.Retry <> RequestOptions.Defaults.Retry then
            jsonObject.Add("retry", Retry.Serialize backendActionOptions.Retry)

        if backendActionOptions.RetryInterval <> RequestOptions.Defaults.RetryInterval then
            jsonObject.Add("retryInterval", backendActionOptions.RetryInterval.TotalMilliseconds)

        if backendActionOptions.RetryScaler <> RequestOptions.Defaults.RetryScaler then
            jsonObject.Add("retryScaler", backendActionOptions.RetryScaler)

        if backendActionOptions.RetryMaxWait <> RequestOptions.Defaults.RetryMaxWait then
            jsonObject.Add("retryMaxWait", backendActionOptions.RetryMaxWait.TotalMilliseconds)

        if backendActionOptions.RetryMaxCount <> RequestOptions.Defaults.RetryMaxCount then
            jsonObject.Add("retryMaxCount", backendActionOptions.RetryMaxCount)

        if backendActionOptions.RequestCancellation <> RequestOptions.Defaults.RequestCancellation then
            let requestCancellation = backendActionOptions.RequestCancellation |> RequestCancellation.Serialize
            jsonObject.Add("requestCancellation", requestCancellation)

        let options = JsonSerializerOptions()
        options.WriteIndented <- false
        HttpUtility.HtmlEncode(jsonObject.ToJsonString(options))

type Debounce =
    { TimeSpan:TimeSpan
      Leading:bool
      NoTrailing:bool }
    static member inline With (timeSpan:TimeSpan, ?leading:bool, ?noTrailing:bool) =
        { TimeSpan = timeSpan; Leading = (defaultArg leading false); NoTrailing = (defaultArg noTrailing false) }
    static member inline With (milliseconds:float, ?leading:bool, ?noTrailing:bool) =
        { TimeSpan = TimeSpan.FromMilliseconds(milliseconds); Leading = (defaultArg leading false); NoTrailing = (defaultArg noTrailing false) }

type Throttle =
    { TimeSpan:TimeSpan
      NoLeading:bool
      Trailing:bool }
    static member inline With (timeSpan:TimeSpan, ?noLeading:bool, ?trailing:bool) =
        { TimeSpan = timeSpan; NoLeading = (defaultArg noLeading false); Trailing = (defaultArg trailing false) }
    static member inline With (milliseconds:float, ?noLeading:bool, ?trailing:bool) =
        { TimeSpan = TimeSpan.FromMilliseconds(milliseconds); NoLeading = (defaultArg noLeading false); Trailing = (defaultArg trailing false) }

type OnEventModifier =
    /// Trigger event once. Can only be used with the built-in events
    | Once
    /// Do not call `preventDefault` on the event listener. Can only be used with the built-in events
    | Passive
    /// Use a capture event listener. Can only be used with the built-in events
    | Capture
    /// Delay the event listener by a Timespan
    | Delay of TimeSpan
    /// Delay the event listener by milliseconds
    | DelayMs of int
    /// Debounce the event listener; new events after an initial event, within a TimeSpan, are ignored.
    | Debounce of Debounce
    /// Throttle the event listener; only fires the last event within a TimeSpan.
    | Throttle of Throttle
    /// Attaches the event listener to the window element.
    | Window
    /// Attaches the event listener to the document.
    | Document
    /// Triggers the event when it occurs outside the element.
    | Outside
    /// Call `preventDefault` on the event listener
    | Prevent
    /// Calls `stopPropagation` on the event listener.
    | Stop
    /// Wrap the expression in document.startViewTransition(), if View Transition API is available
    | ViewTransition

/// The casing Datastar gives to the name an attribute creates. Write it with its type name, e.g. CaseStyle.Snake
[<RequireQualifiedAccess>]
type CaseStyle =
    /// mySignal
    | Camel
    /// my-signal
    | Kebab
    /// my_signal
    | Snake
    /// MySignal
    | Pascal
    with
    static member Serialize (caseStyle:CaseStyle) =
        match caseStyle with
        | CaseStyle.Camel -> "camel"
        | CaseStyle.Kebab -> "kebab"
        | CaseStyle.Snake -> "snake"
        | CaseStyle.Pascal -> "pascal"

/// <summary>
/// Modifier for a DsAttr. &lt;data-...__Name.Tag.Tag=...&gt;
/// </summary>
type DsAttrModifier =
    { Name:string
      Tags:string list }
    with
    static member inline Delay (delay:TimeSpan) =
        { Name = "delay"; Tags = [ $"{delay.TotalMilliseconds}ms" ] }

    static member inline DelayMs (delay:int) =
        { Name = "delay"; Tags = [ $"{delay}ms" ] }

    static member inline DurationMs (duration:int, leading:bool) =
        { Name = "duration"
          Tags = [
              $"{duration}ms"
              if leading then "leading"
          ] }

    static member inline Throttle (throttle:Throttle) =
        { Name = "throttle"
          Tags = [
            $"{throttle.TimeSpan.TotalMilliseconds}ms"
            if throttle.NoLeading then "noleading"
            if throttle.Trailing then "trailing"
          ] }

    static member inline Debounce (debounce:Debounce) =
        { Name = "debounce"
          Tags = [
            $"{debounce.TimeSpan.TotalMilliseconds}ms"
            if debounce.Leading then "leading"
            if debounce.NoTrailing then "notrailing"
          ] }

    static member inline Threshold (threshold:int) =
        { Name = "threshold"
          Tags = [ $"{threshold}" ] }

    static member inline OnEventModifier (onEventModifier:OnEventModifier) =
        match onEventModifier with
        | Once -> { Name = "once"; Tags = [] }
        | Passive -> { Name = "passive"; Tags = [] }
        | Capture -> { Name = "capture"; Tags = [] }
        | Delay delay -> (DsAttrModifier.Delay delay)
        | DelayMs ms -> { Name = "delay"; Tags = [ $"{ms}ms" ] }
        | Debounce debounce -> (DsAttrModifier.Debounce debounce)
        | Throttle throttle -> (DsAttrModifier.Throttle throttle)
        | ViewTransition -> { Name = "viewtransition"; Tags = [] }
        | Window -> { Name = "window"; Tags = [] }
        | Document -> { Name = "document"; Tags = [] }
        | Outside -> { Name = "outside"; Tags = [] }
        | Prevent -> { Name = "prevent"; Tags = [] }
        | Stop -> { Name = "stop"; Tags = [] }

/// <summary>
/// &lt;data-Name-Target__Modifiers="Value"&gt;
/// </summary>
type DsAttr =
    { Name:string
      Target:string voption
      Modifiers:DsAttrModifier list
      HasCaseModifier:bool
      Value:string voption }
    with
    static member inline start name =
        { Name = name; Target = ValueNone; Modifiers = []; Value = ValueNone; HasCaseModifier = false }

    static member inline startEvent eventName =
        { Name = $"on"; Target = ValueSome eventName; Modifiers = []; Value = ValueNone; HasCaseModifier = false }

    static member inline addTarget name dsAttr=
        { dsAttr with Target = ValueSome name }

    static member inline addSignalPathTarget (signalPath:SignalPath) =
        signalPath
        |> SignalPath.keys
        |> Seq.map SignalPath.kebabValue
        |> String.concat "."
        |> DsAttr.addTarget

    static member inline addModifier modifier  dsAttr =
        { dsAttr with Modifiers = (modifier :: dsAttr.Modifiers) }

    static member inline addModifierOption modifierOption dsAttr =
        match modifierOption with
        | ValueSome modifier -> DsAttr.addModifier modifier dsAttr
        | ValueNone -> dsAttr

    static member inline addModifierName modifierName =
        DsAttr.addModifier { Name = modifierName; Tags = [] }

    static member inline addModifierNameIf modifierName bool dsAttr =
        if bool
        then DsAttr.addModifierName modifierName dsAttr
        else dsAttr

    static member inline addValue (value:string) dsAttr =
        { dsAttr with Value = ValueSome value }

    static member inline generateKey dsAttr =
        StringBuilder()
        |> _.Append(Constants.dataSlugPrefix) |> _.Append('-')
        |> _.Append(dsAttr.Name)
        |> (fun sb ->
            match dsAttr.Target with
            | ValueNone -> sb
            | ValueSome target -> sb.Append(':') |> _.Append(target)
            )
        |> (fun sb ->
            match dsAttr.Modifiers with
            | [] -> sb
            | modifiers ->
                for modifier in modifiers do
                    sb.Append("__") |> _.Append(modifier.Name) |> ignore
                    for tag in modifier.Tags do
                        sb.Append('.') |> _.Append(tag) |> ignore
                sb
            )
        |> _.ToString()

    static member inline create dsAttr =
        let dsAttrKey = dsAttr |> DsAttr.generateKey
        match dsAttr.Value with
        | ValueSome value -> Attr.create dsAttrKey value
        | ValueNone -> Attr.createBool dsAttrKey

    static member inline create (name, ?targetName, ?value, ?hasCaseModifier) =
        { Name = name
          Target = targetName |> Option.toValueOption
          Modifiers = []
          Value = value |> Option.toValueOption
          HasCaseModifier = (defaultArg hasCaseModifier false) }
        |> DsAttr.create

    static member inline createSp (name, signalPath, ?value, ?hasCaseModifier) =
        { Name = name
          Target =
              signalPath
              |> SignalPath.kebabValue
              |> ValueSome
          Modifiers = []
          Value = value |> Option.toValueOption
          HasCaseModifier = (defaultArg hasCaseModifier false) }
        |> DsAttr.create

/// The codec a Rocket component gave a prop. Write it with its type name, e.g. RocketPropType.Number.
/// Rocket may add codecs, so a name this library does not know is kept as Other.
[<RequireQualifiedAccess>]
type RocketPropType =
    | String
    | Number
    | Boolean
    | Date
    | Json
    | Js
    | Binary
    | Array
    | Tuple
    | Object
    | OneOf
    | Custom
    | Other of string

/// Whether a component event is a plain event or a custom event with a payload. Write it with its type name.
[<RequireQualifiedAccess>]
type RocketEventKind =
    | Event
    | CustomEvent
    | Other of string

/// The documentation a component author gave a prop with the docs(...) method of its codec
type RocketPropDocs =
    { Description: string voption
      Label: string voption
      /// The kind of control a tool should show: auto, text, textarea, number, boolean or select
      Control: string voption
      Placeholder: string voption }

/// A prop of a Rocket component, as Rocket described it in a manifest
type RocketManifestProp =
    { Name: string
      /// The attribute the prop is read from, e.g. max-count for maxCount
      Attribute: string
      Type: RocketPropType
      /// The value the prop has when the attribute is missing, as JSON
      Default: JsonElement
      Required: bool
      /// The values a oneOf prop allows
      Values: JsonElement list voption
      Docs: RocketPropDocs voption }

/// A slot of a Rocket component, as its author documented it
type RocketManifestSlot =
    { Name: string
      Description: string voption }

/// An event of a Rocket component, as its author documented it
type RocketManifestEvent =
    { Name: string
      Kind: RocketEventKind
      Bubbles: bool voption
      Composed: bool voption
      Description: string voption }

/// One Rocket component in a manifest
type RocketManifestComponent =
    { Tag: string
      Props: RocketManifestProp list
      Slots: RocketManifestSlot list
      Events: RocketManifestEvent list }

/// What Rocket's publishRocketManifests posts: every component the page defined, sorted by tag
type RocketManifestDocument =
    { Version: int
      GeneratedAt: DateTimeOffset
      Components: RocketManifestComponent list }
