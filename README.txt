Wireless ECR Simulator 1.0 - by Ahmad El Lahib
=================================================

A Windows app for testing the areeba wireless ECR integration server. It plays both sides:

  * MERCHANT (cash register / vending backend), the same calls as the Postman collection:
      POST /access-token/application-token, POST /integration/transaction
      (SALE, PREAUTH, INCREMENTAL_PREAUTH, COMPLETION, VOID, SETTLE),
      GET /integration/trx-info, GET /integration/pos-trx-info, POST /integration/door/open
  * VIRTUAL TERMINAL: a POS on the WebSocket (Login, TRXResult, CONFIRMATION, OpenDoor/DoorResult),
    old or new protocol, so you can test without a device.
  * DOOR (Boonz): a mock merchant "Tap Start" API, and Boonz behaviour (send a PRE-AUTH once the
    door opens).

Build and run
-------------
  Wireless ECR Simulator.cmd   opens it: Start-WirelessEcrSimulator.ps1 compiles src\*.cs in memory
                               (no exe, nothing to install), like the ECR Simulator
  Start-WirelessEcrSimulator.ps1 -Snapshot file.png   renders the window to a PNG and exits
  build.cmd                    optional: builds "Wireless ECR Simulator.exe" (the company antivirus
                               blocks it on this laptop)

Files next to the program
-------------------------
  wireless-ecr-simulator.settings.json   environments and options. Passwords, client secrets and tokens
                                         are encrypted with Windows DPAPI for your Windows user only.
  logs\wireless-ecr-YYYYMMDD.log         everything shown in the Activity panel (passwords masked)

Environments
------------
  Local      http://localhost:8099, ws://localhost:8099/websocket, terminal T0012345 / 1234
  SIT           https://socket-mpos.areeba.com:5002, wss://socket-mpos.areeba.com:5002
  Lebanon PROD  https://socket-mpos.areeba.com:5000, wss://socket-mpos.areeba.com:5000
  Iraq PROD     https://api.areeba.iq/ecr-websocket
  Client secret: put it once in defaults.local.json (copy defaults.local.example.json) and SIT and
  Lebanon PROD start with it filled in, or paste it in the app. defaults.local.json is git-ignored:
  the secret also gets production tokens, so it must never be committed.
  Press "Get application token" to get a token. Every field can be edited; Save keeps it.
  "+ New" copies the selected environment, "Rename" / "Delete" manage them, "PROD" marks a production
  one (red badge, every send asks for confirmation).

Amounts
-------
  Sent exactly as typed, in minor units (100 = 1.00 USD), like the Postman collection.

Virtual terminal
----------------
  Connect logs in with the terminal id and password from the Environment card.
  WARNING: logging in as a real device's terminal id takes that device's session on the server.
  Answer modes: Approve, Decline (code + text, e.g. -1 or a mirrored host code like 1116), Ask me
  (Approve / Decline / Busy / Ignore buttons), Don't answer (the merchant call ends with code 1),
  Busy (POS busy result). Options: answer delay, send every result twice, new protocol (heartbeat 30 s,
  re-sends a result after a reconnect until CONFIRMATION), auto reconnect 1-30 s, "Drop link" (kill the
  socket without a close frame, like losing Wi-Fi).

Fridge door without a door (Boonz)
----------------------------------
  Tick "No door access: act as Boonz". The simulator then sends a PRE-AUTH (amount and delay in the
  card; currency, waiting time and next ECR reference from the transaction card) when:
    1. "Open door (API)" returns responseCode 0, or
    2. "Press START on terminal" (the virtual terminal sends OpenDoor) gets DoorResult 0, or
    3. the server calls the mock Tap Start endpoint (see below).
  Tick "Pretend the door opened" to skip the door call completely: the buttons send the PRE-AUTH at once.
  One door opening reported twice (mock call + DoorResult) sends only one PRE-AUTH (20 s window).

  Mock Tap Start, for a LOCAL integration server: press Start (port 8790), then run the server with
      merchant.ecr.mock-enabled=false
      merchant.ecr.url=http://localhost:8790
      merchant.ecr.secret=<anything; type the same secret in the simulator to check X-Signature>
  The mock answers like weimi24: 200 success, -1 signature error, 7002 timestamp expired, HTTP 500, or
  no answer (the server gives up after merchant.ecr.timeout-seconds, default 10).
  A server with merchant.ecr.mock-enabled=true (the default) opens the door itself; then trigger 1 or 2
  sends the PRE-AUTH.

Response codes
--------------
  0 approved/success, 1 sent but no answer within waitingTime (check with trx-info / pos-trx-info),
  -1 declined/failed on the POS, -2 missing parameter, -3 check service unavailable, -4 invalid
  requestID, -5 merchant refused to open the door, 404 terminal not logged in, 450 connection with POS
  lost, 500 wrong password, 5003 transaction not found, 4-digit codes = host code mirrored by the POS
  (TMS ecr.mirrorResponseCode).

Source
------
  src\Program.cs          entry point, --snapshot
  src\MainWindow.xaml     the window (loaded at runtime, embedded in the exe)
  src\MainController.cs   window logic
  src\IntegrationApi.cs   REST calls
  src\VirtualTerminal.cs  WebSocket POS
  src\MockMerchant.cs     Tap Start mock (HttpListener)
  src\Settings.cs, Json.cs, ResponseCodes.cs
  C# 5 / .NET Framework 4.5+, WPF, no NuGet packages.
