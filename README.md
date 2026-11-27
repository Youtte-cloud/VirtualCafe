# Virtual Cafe — LLM-Enhanced Interactive Cafe

Virtual Cafe is a Unity proof of concept that combines a virtual cafe with an AI barista. Customers can explore the environment, order drinks directly, or receive recommendations based on their preferences and the current menu and inventory.

## Main Features

- Indoor cafe and outdoor courtyard.
- Desktop player movement, counter interaction and seating.
- Direct ordering with drink, bean, milk and cup-size options.
- Drink and ingredient previews.
- Mixed-drink shopping cart with price and availability checks.
- AI recommendations through text or voice input.
- Multi-turn conversations for refining preferences.
- Chinese, English and Japanese interfaces.
- Coffee preparation animations with synchronized sound effects.
- Order confirmation, pickup numbers and drink collection.
- Multiple carried orders with a total limit of five cups.
- Table placement, coffee drinking and capacity reminders.

## LLM Integration

The AI barista receives a system prompt and contextual information, including:

- The current menu and permitted drink configurations.
- Prices, ingredient availability and stock quantities.
- Ingredient flavor descriptions.
- Customer preferences and conversation history.
- The current drink selection and the latest confirmed order, when available.

The model returns a structured JSON response containing its reply and, where appropriate, a drink recommendation.

Unity validates the response before applying the selection and updating the drink preview and product highlight. Customers confirm purchases through the interface. The ordering system checks availability again and deducts ingredients after successful confirmation.

Later recommendation requests include the updated inventory, connecting the conversation to changes in the cafe.

## Menu

The demonstration menu includes:

- Blue Mountain Black Coffee.
- Geisha Black Coffee.
- Kona Black Coffee.
- Latte with selectable beans and whole, skim or oat milk.

Drinks are available in 400 ml and 500 ml sizes. Prices and availability depend on the selected configuration and current inventory.

## Preparation and Customer Flow

1. Approach the counter and choose direct ordering or AI recommendations.
2. Configure drinks and review the shopping cart.
3. Confirm the order and receive a pickup number.
4. Watch the coffee preparation process.
5. Collect the completed drinks.
6. Carry drinks to a seating area and place them on a table.
7. Use the drinking interaction to consume coffee.

Preparation includes bean pouring, grinding, drawer movement, grounds transfer, extraction and milk pouring for lattes.

Customers can carry up to five cups across multiple orders. When capacity is full, the barista asks customers to place drinks before ordering again. If a table cannot accommodate the drinks, the remaining drinks stay in the customer's carried orders.

## Development Environment

- Unity 2022.3.62f3.
- C#.
- Windows desktop.
- DeepSeek-compatible chat completions API.

## Getting Started

1. Clone or download the repository.
2. Open the project with Unity 2022.3.62f3.
3. Open the `CafeMain` scene.
4. In the Unity menu, select **Virtual Cafe > LLM Settings**.
5. Enter the API endpoint, model name and your own API key, then save.
6. Enter Play mode.

LLM configuration must also be available to a standalone build. Check `CafeLlmLocalSettings.cs` for its configuration file location.

## Controls

- **W / A / S / D**: Move.
- **Mouse**: Look around or interact with the interface.
- **F**: Interact with the counter when prompted.
- **E**: Collect drinks or sit down / stand up when prompted.
- **G**: Drink coffee when available.
- **Esc**: Close the interface and return to movement.

Follow the on-screen prompts for each interaction.

## LLM Interaction Logs

Completed LLM requests are saved as JSON files under:

`Application.persistentDataPath/CafeLLM`

Each trace includes:

- The request context sent to the model.
- The raw API response.
- The model's output.
- Validation results and the parsed recommendation.
- Request duration.

These records can be used to inspect how customer input and scene context lead to a recommendation. Voice requests appear as transcribed text in the LLM request.

## Project Structure

- `Assets/Scripts/Recommendation`: LLM requests, response validation, recommendation interface and speech integration.
- `Assets/Scripts/Inventory`: Menu, recipes, pricing and inventory services.
- `Assets/Scripts/FrontDesk`: Drink preparation and carried-drink interactions.
- `Assets/Scripts/Player`: Desktop movement and seating interactions.

## Configuration

Use your own API credentials. Keep API keys, passwords and local credential files out of version control.

Inventory is stored locally between sessions. Check the inventory configuration before running a demonstration.

## Course Project

Developed for COMP5140: LLM-Enhanced Interactive Metaverse Experience.
