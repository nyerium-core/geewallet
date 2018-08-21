namespace GWallet.Frontend.XF

open Xamarin.Forms

type App() =
    inherit Application(MainPage = Initialization.LandingPage())

    override this.OnSleep(): unit =
        Initialization.GlobalState.Awake <- false
        Async.CancelDefaultToken()

    override this.OnResume(): unit =
        Initialization.GlobalState.Awake <- true

        let maybeBalancesPage =
            match this.MainPage with
            | :? BalancesPage as balancesPage ->
                Some balancesPage
            | :? NavigationPage as navPage ->
                match navPage.RootPage with
                | :? BalancesPage as balancesPage ->
                    Some balancesPage
                | _ ->
                    None
            | _ ->
                None

        match maybeBalancesPage with
        | Some balancesPage ->
            balancesPage.StartBalanceRefreshCycle CycleStart.ImmediateForAll
        | None -> ()
