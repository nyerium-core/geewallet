namespace GWallet.Frontend.XF

open System

type GlobalState() =
    let lockObject = Object()
    let mutable awake = true
    member internal this.Awake
        with set value = lock lockObject (fun _ -> awake <- value)

    interface FrontendHelpers.IGlobalAppState with
        member this.Awake
            with get() = lock lockObject (fun _ -> awake)
