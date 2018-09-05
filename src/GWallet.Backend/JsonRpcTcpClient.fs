﻿namespace GWallet.Backend

open System
open System.Linq
open System.Net
open System.Net.Sockets
open System.Threading.Tasks

type internal UnhandledSocketException =
    inherit Exception

    new(socketErrorCode: int, innerException: Exception) =
        { inherit Exception(sprintf "GWallet not prepared for this SocketException with ErrorCode[%d]" socketErrorCode,
                                    innerException) }

type ServerRefusedException(message:string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type ServerTimedOutException(message:string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type ProtocolGlitchException(message: string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type ServerCannotBeResolvedException =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException

   new(message) = { inherit JsonRpcSharp.ConnectionUnsuccessfulException(message) }
   new(message:string, innerException: Exception) = { inherit JsonRpcSharp.ConnectionUnsuccessfulException(message, innerException) }

type ServerUnreachableException(message:string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type JsonRpcTcpClient (host: string, port: int) =
    let ResolveAsync (hostName: string): Task<IPAddress> =
        // FIXME: loop over all addresses?
        Task.Run(fun _ ->
            let addressList = Dns.GetHostEntry(hostName).AddressList
            let firstAddress = addressList.[0]
            if (firstAddress.AddressFamily = AddressFamily.InterNetworkV6) then
                let ipv4Addresses = addressList.Where(fun addr -> addr.AddressFamily <> AddressFamily.InterNetworkV6)
                match Seq.tryHead ipv4Addresses with
                | None ->
                    Console.Error.WriteLine
                        (sprintf "WARNING: host '%s' DNS resolution returned only IPv6 address(es) (first: %s)"
                                 hostName (firstAddress.ToString()))
                | Some firstIpV4Address ->
                    Console.Error.WriteLine
                        (sprintf "WARNING: host '%s' DNS resolution returned IPv6 address as first entry (%s) but has also IPv4 (%s)"
                                 hostName (firstAddress.ToString()) (firstIpV4Address.ToString()))
            firstAddress
        )

    let exceptionMsg = "JsonRpcSharp faced some problem when trying communication"
    let ResolveHost(): IPAddress =
        try
            let resolveTask = ResolveAsync host
            if not (resolveTask.Wait Config.DEFAULT_NETWORK_TIMEOUT) then
                raise(ServerCannotBeResolvedException(exceptionMsg))
            resolveTask.Result
        with
        | ex ->
            let socketException = FSharpUtil.FindException<SocketException>(ex)
            if (socketException.IsNone) then
                reraise()
            if (socketException.Value.ErrorCode = int SocketError.HostNotFound ||
                socketException.Value.ErrorCode = int SocketError.TryAgain) then
                raise(ServerCannotBeResolvedException(exceptionMsg, ex))
            raise(UnhandledSocketException(socketException.Value.ErrorCode, ex))

    let rpcTcpClient = new JsonRpcSharp.TcpClient(fun _ -> (ResolveHost(),port))

    member self.Request (request: string): string =
        try
            rpcTcpClient.Request request
        with
        | :? JsonRpcSharp.ConnectionUnsuccessfulException ->
            reraise()

        // FIXME: we should log this one on Sentry as a warning because it's really strange, I bet it's a bug
        // on Mono that could maybe go away with higher versions of it (higher versions of Xamarin-Android), see
        // git blame to look at the whole stacktrace (ex.ToString())
        | :? NotSupportedException as nse ->
            raise(ProtocolGlitchException(exceptionMsg, nse))

        | ex ->
            let socketException = FSharpUtil.FindException<SocketException>(ex)
            if (socketException.IsNone) then
                reraise()

            if (socketException.Value.ErrorCode = int SocketError.ConnectionRefused) then
                raise(ServerRefusedException(exceptionMsg, ex))

            if (socketException.Value.ErrorCode = int SocketError.TimedOut) then
                raise(ServerTimedOutException(exceptionMsg, ex))

            // probably misleading errorCode (see fixed mono bug: https://github.com/mono/mono/pull/8041 )
            // TODO: remove this when Mono X.Y (where X.Y=version to introduce this bugfix) is stable
            //       everywhere (probably 8 years from now?), and see if we catch it again in sentry
            if (socketException.Value.ErrorCode = int SocketError.AddressFamilyNotSupported) then
                raise(ServerUnreachableException(exceptionMsg, ex))

            if (socketException.Value.ErrorCode = int SocketError.HostUnreachable) then
                raise(ServerUnreachableException(exceptionMsg, ex))
            if (socketException.Value.ErrorCode = int SocketError.NetworkUnreachable) then
                raise(ServerUnreachableException(exceptionMsg, ex))
            if (socketException.Value.ErrorCode = int SocketError.AddressNotAvailable) then
                raise(ServerUnreachableException(exceptionMsg, ex))

            raise(UnhandledSocketException(socketException.Value.ErrorCode, ex))

