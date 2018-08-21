namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

module DummyLoadingPageHelper =

    [<Literal>]
    let CtorWarning =
        "DO NOT USE THIS! This paramaterless constructor is only here to allow the VS designer to render page"

    let DummyFuncToRaiseException(): FrontendHelpers.IGlobalAppState =
#if !DEBUG // if we put the failwith in DEBUG mode, then the VS designer crashes with it when trying to render
        failwith CtorWarning
#endif
        GlobalState() :> FrontendHelpers.IGlobalAppState

type LoadingPage(state: FrontendHelpers.IGlobalAppState) as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let allAccounts = Account.GetAllActiveAccounts()
    let normalAccounts = allAccounts.OfType<NormalAccount>() |> List.ofSeq
                         |> List.map (fun account -> account :> IAccount)
    let readOnlyAccounts = allAccounts.OfType<ReadOnlyAccount>() |> List.ofSeq
                           |> List.map (fun account -> account :> IAccount)

    do
        this.Init()

    [<Obsolete(DummyLoadingPageHelper.CtorWarning)>]
    new() = LoadingPage(DummyLoadingPageHelper.DummyFuncToRaiseException())

    member this.Init (): unit =

        let normalAccountsWithLabels = FrontendHelpers.CreateWidgetsForAccounts normalAccounts
        let allNormalAccountBalancesJob =
            seq {
                for normalAccount,accountBalanceLabel,fiatBalanceLabel in normalAccountsWithLabels do
                    let balanceJob =
                        FrontendHelpers.UpdateBalanceAsync normalAccount accountBalanceLabel fiatBalanceLabel
                    yield balanceJob
            } |> Async.Parallel

        let readOnlyAccountsWithLabels = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts
        let readOnlyAccountBalancesJob = FrontendHelpers.UpdateCachedBalancesAsync readOnlyAccountsWithLabels

        let populateGrid = async {
            let allBalancesJob = Async.Parallel(allNormalAccountBalancesJob::(readOnlyAccountBalancesJob::[]))
            let! allResolvedBalances = allBalancesJob
            let allResolvedNormalAccountBalances = allResolvedBalances.ElementAt(0)
            let allResolvedReadOnlyBalances = allResolvedBalances.ElementAt(1)

            let balancesPage = BalancesPage(state, allResolvedNormalAccountBalances, allResolvedReadOnlyBalances, false)
            FrontendHelpers.SwitchToNewPageDiscardingCurrentOne this balancesPage
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

