﻿namespace GWallet.Frontend.XamForms.Mac

open AppKit

module main =
    [<EntryPoint>]
    let main args =
        NSApplication.Init()
        NSApplication.SharedApplication.Delegate <- new AppDelegate()
        NSApplication.Main(args)
        0