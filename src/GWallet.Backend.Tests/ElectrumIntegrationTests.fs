﻿namespace GWallet.Backend.Tests

open System
open System.Linq

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin

// TODO: move this to its own file
[<TestFixture>]
type ElectrumServerUnitTests() =

    [<Test>]
    member __.``filters electrum BTC servers``() =
        for electrumServer in ElectrumServerSeedList.DefaultBtcList do
            Assert.That (electrumServer.UnencryptedPort, Is.Not.EqualTo(None),
                sprintf "BTC servers list should be filtered against only-TLS compatible servers, but %s was found"
                        electrumServer.Fqdn)

            Assert.That (electrumServer.Fqdn, Is.Not.StringEnding(".onion"),
                sprintf "BTC servers list should be filtered against onion servers, but %s was found"
                        electrumServer.Fqdn)

    [<Test>]
    member __.``filters electrum LTC servers``() =
        for electrumServer in ElectrumServerSeedList.DefaultLtcList do
            Assert.That (electrumServer.UnencryptedPort, Is.Not.EqualTo(None),
                sprintf "BTC servers list should be filtered against only-TLS compatible servers, but %s was found"
                        electrumServer.Fqdn)

            Assert.That (electrumServer.Fqdn, Is.Not.StringEnding(".onion"),
                sprintf "BTC servers list should be filtered against onion servers, but %s was found"
                        electrumServer.Fqdn)

[<TestFixture>]
type ElectrumIntegrationTests() =

    // probably a satoshi address because it was used in blockheight 2 and is unspent yet
    let SATOSHI_ADDRESS =
        // funny that it almost begins with "1HoDL"
        "1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1"

    // https://medium.com/@SatoshiLite/satoshilite-1e2dad89a017
    let LTC_GENESIS_BLOCK_ADDRESS = "Ler4HNAEfwYhBmGXcFP2Po1NpRUEiK8km2"

    let CheckServerIsReachable (electrumServer: ElectrumServer)
                               (currency: Currency)
                               (maybeFilter: Option<ElectrumServer -> bool>)
                               : Async<Option<ElectrumServer>> = async {

        let address =
            match currency with
            | Currency.BTC -> SATOSHI_ADDRESS
            | Currency.LTC -> LTC_GENESIS_BLOCK_ADDRESS
            | _ -> failwith "Tests not ready for this currency"

        let innerCheck server =
            // this try-with block is similar to the one in UtxoCoinAccount, where it rethrows as
            // ElectrumServerDiscarded error, but here we catch 2 of the 3 errors that are caught there
            // because we want the server incompatibilities to show up here (even if GWallet clients bypass
            // them in order not to crash)
            try
                let electrumClient = ElectrumClient electrumServer
                let balance = electrumClient.GetBalance address

                // if these ancient addresses get withdrawals it would be interesting in the crypto space...
                // so let's make the test check a balance like this which is unlikely to change
                Assert.That(balance.Confirmed, Is.Not.LessThan(998292))

                Console.WriteLine (sprintf "%A server %s is reachable" currency server.Fqdn)
                Some electrumServer
            with
            | :? JsonRpcSharp.ConnectionUnsuccessfulException as ex ->
                // to make sure this exception type is an abstract class
                Assert.That(ex.GetType(), Is.Not.EqualTo(typeof<JsonRpcSharp.ConnectionUnsuccessfulException>))

                Console.WriteLine (sprintf "%A server %s is unreachable" currency server.Fqdn)
                None
            | :? ElectrumServerReturningInternalErrorException as ex ->
                Console.WriteLine (sprintf "%A server %s is unhealthy" currency server.Fqdn)
                None

        match maybeFilter with
        | Some filterFunc ->
            if (filterFunc electrumServer) then
                return innerCheck electrumServer
            else
                return None
        | _ ->
            return innerCheck electrumServer

        }

    let CheckElectrumServersConnection electrumServers currency =
        let reachServerTasks = seq {
            for electrumServer in electrumServers do
                yield CheckServerIsReachable electrumServer currency None
        }
        let reachableServers = Async.Parallel reachServerTasks |> Async.RunSynchronously |> List.ofArray
        let reachableServersCount = reachableServers.Count(fun server -> server.IsSome)
        Console.WriteLine (sprintf "%d %A servers were reachable" reachableServersCount currency)
        Assert.That(reachableServersCount, Is.GreaterThan(1))

    [<Test>]
    member __.``can connect to some electrum BTC servers``() =
        CheckElectrumServersConnection ElectrumServerSeedList.DefaultBtcList Currency.BTC

    [<Test>]
    member __.``can connect to some electrum LTC servers``() =
        CheckElectrumServersConnection ElectrumServerSeedList.DefaultLtcList Currency.LTC
