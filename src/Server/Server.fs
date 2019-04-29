open System.IO
open System.Threading.Tasks

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.DependencyInjection
open FSharp.Control.Tasks.V2
open Giraffe
open Saturn
open Shared

open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let tryGetEnv = System.Environment.GetEnvironmentVariable >> function null | "" -> None | x -> Some x

let publicPath = Path.GetFullPath "../Client/public"

let port =
    "SERVER_PORT"
    |> tryGetEnv |> Option.map uint16 |> Option.defaultValue 8085us

let getCacheFromContext f (context: HttpContext) =
    let cache = context.GetService<IMemoryCache>()
    f cache

let longRunningCalc() =
        lazy
            printfn "Calculating..."
            let x = [ 1 .. 20000000 ] |> List.fold (fun x y -> (x + y) % 99) 0
            { Value = x }

let key = 1

let cachedLongRunningCalc (cache: IMemoryCache) () =
    task {
        printfn "Checking cache"
        match cache.TryGetValue key with
        | true, (:? Lazy<Counter> as lazyCounter ) ->
            printfn "From cache"
            return lazyCounter.Value
        | _ ->
            printfn "A"
            let lazyCounter = longRunningCalc()
            printfn "B"
            cache.Set(key, lazyCounter) |> ignore
            printfn "C"
            let counter = lazyCounter.Value
            printfn "D"
            return counter
    }

let cachedCounterApi cache = {
    initialCounter = cachedLongRunningCalc cache >> Async.AwaitTask
}

let webApp =
    Remoting.createApi()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromContext (getCacheFromContext cachedCounterApi)
    |> Remoting.buildHttpHandler

let app = application {
    url ("http://0.0.0.0:" + port.ToString() + "/")
    use_router webApp
    memory_cache
    use_static publicPath
    use_gzip
    service_config (fun s -> s.AddMemoryCache())
}

run app
