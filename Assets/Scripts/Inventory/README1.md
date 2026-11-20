# 库存与菜单接入

## ① 菜单、配方与计价

菜单为蓝山、瑰夏、科纳三款固定豆种黑咖啡，以及自由选择豆奶的拿铁，仅有400/500ml两种杯型。

| 饮品/杯型 | 豆(g) | 萃取液目标(g) | 水(ml) | 冰(g) | 奶(ml) |
|---|---:|---:|---:|---:|---:|
| 黑咖啡400 | 18 | 30 | 150 | 200 | 0 |
| 黑咖啡500 | 18 | 36 | 220 | 230 | 0 |
| 拿铁400 | 20 | 40 | 0 | 0 | 250 |
| 拿铁500 | 20 | 40 | 0 | 0 | 330 |

以上为项目演示参数；只扣豆和奶，萃取液是制作目标，水和冰供动画使用。

豆每5g：蓝山300分、瑰夏350分、科纳400分；奶每100ml：全脂300分、脱脂350分、燕麦400分。按实际用量比例计价，黑咖啡加水冰费300分，500ml统一加200分；拿铁无水冰费，均不收萃取液费用。全程整数分。全脂和脱脂奶含牛奶；燕麦奶不保证无过敏风险，咖啡风味描述仅为倾向。

## ② 文件作用

### 库存模块：`Assets/Scripts/Inventory/`

| 文件 | 作用 |
|---|---|
| `InventoryModels.cs` | 原料定义、配方、库存数量、饮品请求及库存推荐快照的数据结构。 |
| `InventoryService.cs` | 原料数量查询、配方校验、整笔需求汇总、统一扣减与保存、库存变化通知；包含 JSON 文件读写类。 |
| `CafeMenuService.cs` | 四款菜单的选项校验、价格计算、实时可售判断、动画配方查询及推荐快照；确认时调用库存服务。也定义菜单、选择、报价等数据结构。 |
| `CafeRuntimeServices.cs` | 为场景模块提供同一份运行时库存与菜单服务，统一通过共享入口访问。 |
| `CafeMenuSceneAdapter.cs` | 场景按钮交互入口，维护当前选择，输出价格、可售状态与确认结果事件。 |
| `README.md` | 本接入说明。 |

### 固定数据：`Assets/Resources/Cafe/`

| 文件 | 作用 |
|---|---|
| `menu.json` | 定义菜单饮品、可选豆奶、杯型，以及水冰费和大杯加价。 |
| `drink-recipes.json` | 定义每种饮品/杯型的原料用量，以及萃取液、水和冰的制作目标。 |
| `inventory-catalog.json` | 定义八项原料的名称、单位、风味倾向、过敏原说明、计价单位/单价及初始库存种子；不是实时库存文件。 |

各文件对应的 `.meta` 是 Unity 资源标识与导入设置文件，应与资源一同保留。

## ③ 按使用方接入

`CafeMenuSceneAdapter` 是场景按钮的交互入口，挂在菜单控制对象（如 `CafeMenuController`）上。选择按钮绑定它的 `SelectDrink`、`SelectBean`、`SelectMilk`、`SelectCupSize`、`SetQuantity` 方法；确认按钮绑定 `ConfirmCurrentSelection()`。打开菜单时可以调用 `RefreshDisplay()`。

Adapter 目前只处理**当前选择**。整张菜单表的价格和可售状态，需要 UI 脚本调用 `CafeRuntimeServices.Menu.GetMenuOffers()` 填充，不会由 Adapter 自动生成。

运行时在 Unity 主线程使用共享实例 `CafeRuntimeServices.Menu`，不要在每个按钮或刷新时重复创建服务。以下查询分别服务于菜单 UI、当前选择、动画及推荐模块，并非都只供 LLM 使用。

### 场景菜单 UI：整张菜单表

`GetMenuOffers()` 无参数，返回 `MenuOffer[]`：当前有24种单杯组合，每项包含 `selection`（饮品、杯型、豆奶选项）、`price`（整数分价格）、`available`（可售状态）和 `reason`（原因）。UI 按需要分组展示。

打开菜单时查询一次；收到共享服务的 `InventoryChanged` 无参数事件后重新查询。UI 禁用或销毁时取消订阅。返回的是当时的状态，不预占库存。

```csharp
var menu = CafeRuntimeServices.Menu;
System.Action refresh = () => {
    var offers = menu.GetMenuOffers();
    // 使用 offers 填充整张菜单表。
};
menu.InventoryChanged += refresh;
refresh(); // 打开菜单时首次填充
// UI 禁用/销毁时：menu.InventoryChanged -= refresh;
```

### 当前选择 UI：Adapter 事件

在 Adapter Inspector 中用事件的“动态参数”连接已有 UI：

| 事件 | 输出与用途 |
|---|---|
| `PriceChanged(string)` | 当前选择及数量的总价文本，无效选择为 `—` |
| `StatusChanged(string)` | 可制作状态、库存不足或查询失败原因 |
| `CanConfirmChanged(bool)` | 是否允许确认，可绑定按钮 `interactable` |
| `ResultChanged(string)` | 最近一次确认的成功或失败说明 |
| `ConfirmationCompleted(bool, string)` | 确认成功标志及说明，供后续流程使用 |

选择变化、Play 中组件启用、共享库存变化时自动刷新；打开菜单或代码订阅事件后可调用 `RefreshDisplay()` 补一次显示。价格和可售状态是查询结果，确认结果只在主动确认后产生。自行添加的事件监听应在禁用/销毁时移除。

初始选择为400ml蓝山黑咖啡1杯；拿铁可选三种豆、三种奶。黑咖啡固定豆种，UI 应隐藏或禁用自由豆奶按钮。当前选择也可通过 `CurrentSelection` 副本读取。

### 制作动画：配方查询

`CafeRuntimeServices.Menu.TryGetRecipe(selection, out DrinkRecipe recipe, out string reason)` 接收 `MenuSelection`，返回是否找到合法配方。成功时 `recipe` 是单杯配方副本，包含豆奶需求、萃取液目标、水和冰用量；失败时返回原因。多杯制作由动画模块按数量组织。

饮品、杯型或豆奶选项变化后重新查询；开始制作时使用用户已确认的选择，避免误用后来修改的当前选择。查询不扣库存，也不因缺货而拒绝返回合法配方。

### LLM 推荐：风味与实时库存快照

`CafeRuntimeServices.Menu.GetRecommendationSnapshotJson()` 无参数，返回 JSON 字符串，包含菜单、价格、杯型配方、风味、实时可用量及可售状态，不包含汽水。

每次发起推荐前重新获取；库存变化后旧快照可能过期。LLM 只读取快照并提出建议，**不得直接调用确认接口或扣库存**。

### Adapter／订单程序：确认购买（会扣库存）

当前选择由按钮调用 `Adapter.ConfirmCurrentSelection()`（无参数），通过上述结果事件通知 UI。整笔订单由订单程序调用 `CafeRuntimeServices.Menu.TryConfirmOrder(selections, out string reason)`，其中 `selections` 为 `IReadOnlyList<MenuSelection>`；返回成功标志及结果说明。

**确认方法会重新汇总整笔原料需求，全部足够才扣库存并保存。** 任一选择无效、库存不足或保存失败时整笔不扣。只能在用户明确确认购买后调用，不能接到菜单打开、刷新或推荐事件上；此前的可售状态不能替代确认校验。成功后的库存变化通知用于重新填充菜单表。
