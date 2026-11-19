# D cafe UI with shared B inventory

Open CafeMain in Unity 2022.3.62f3 and press Play. UI is created at runtime. English labels avoid Chinese font dependencies. Assistant remains a clearly labelled local MOCK: no LLM API is connected.

## Shared menu
Uses CafeRuntimeServices.Menu, not separate drink-count stock. Four menu items: Blue Mountain, Geisha and Kona black coffees, and Latte. Choose 400/500ml; latte supports the configured bean/milk options. Prices come from TryGetPrice in integer cents; availability comes from IsAvailable. Confirm calls TryConfirmOrder and saves shared ingredient stock. Reset UI clears chat and pending choice, never resets saved stock.

## Request safety
Leaving Chat cancels the mock coroutine, invalidates its request version and clears busy state. Selecting a drink also cancels it. A reply may populate a pending choice only while Chat remains active. Confirmation captures a copy of the selection before calling shared services. InventoryChanged is subscribed once and unsubscribed on destruction.

## Manual Play checks still required
1. Send a mock request, immediately click Menu, select Kona; after a second the order must still be Kona.
2. Send then reset; obsolete reply must not appear.
3. Open each menu item. Change 400/500ml and quantity: shared price updates.
4. Latte: change all bean and milk options. Black coffees must not show free bean/milk selectors.
5. Cancel: no inventory deduction. Confirm: shared ingredients deducted once, updated offers displayed.
6. Exhaust an ingredient using confirmed orders; affected combinations must become unavailable. Confirmation rechecks stock.
7. /fail: displays mock error, no inventory change; direct ordering works.
8. no caffeine: no invented caffeine-free menu item.
9. Reset UI and restart Play: saved ingredient inventory is not replenished.
10. Ask same after buying: use previous confirmed choice if available, otherwise available alternative.

## ABC handoff
B inventory/menu scripts and Resources/Cafe data were copied unchanged from the locally available origin/main snapshot into this working tree. This does not merge branch history; include the new inventory/resources files and their meta files when committing unless main has first been merged normally.
A: subscribe to ProductSelected(menuItemId) for scene highlight. This UI does not implement scene highlights or movement.
C: replace MockReply with real async recommendations. Read GetRecommendationSnapshotJson immediately before requests. Preserve cancellation/version checks; validate all returned options and availability. Never confirm an order from a recommendation callback.
D: own navigation, confirmation, waiting/errors and manual integration tests. Do not add another inventory service or restore the old six-drink demo menu.

Verification: all current C# scripts compiled together using the installed Unity Mono C# compiler against Unity engine and UI assemblies. Visual layout and Play interaction checks have not yet been run by the agent.
