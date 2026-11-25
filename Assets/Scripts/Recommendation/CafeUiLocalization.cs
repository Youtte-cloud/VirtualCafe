using System;
using System.Text.RegularExpressions;

namespace VirtualCafe.Recommendation
{
    // One language setting is shared by panels, dialogue controls and the player HUD.
    public static class CafeUiLocalization
    {
        public static int LanguageIndex { get; set; } = 1;
        public static string LanguageCode => LanguageIndex == 0 ? "zh-CN" : LanguageIndex == 2 ? "ja-JP" : "en-US";
        private static readonly string[,] Phrases = {
            { "Interested in this coffee?", "想尝尝这杯吗？", "こちらのコーヒーはいかがですか？" },
            { "Order this cup", "点这杯", "この一杯を注文" },
            { "Made with milk; you can choose your bean and milk.", "这款加入了奶，可以选择咖啡豆和奶种。", "ミルク入りです。豆とミルクをお選びいただけます。" },
            { "This drink is sold out. Shall I help you choose another?", "这款暂时售罄了，要帮你推荐其他咖啡吗？", "ただいま売り切れです。別のコーヒーをご案内しましょうか？" },
            { "Typically floral and fruity; actual flavors vary with origin, batch, roast and brewing and are not guaranteed.", "通常偏花香与果香；实际风味随产地、批次、烘焙及冲煮而变化。", "一般に花や果実のような香りが特徴です。風味は産地、ロット、焙煎、抽出により異なります。" },
            { "Typically mellow and balanced with nutty sweetness; actual flavors vary with batch, roast and brewing and are not guaranteed.", "通常柔和均衡，带有坚果般的甜感；实际风味随批次、烘焙及冲煮而变化。", "一般にまろやかでバランスがよく、ナッツのような甘さが特徴です。風味はロット、焙煎、抽出により異なります。" },
            { "Typically mellow and sweet with light fruity acidity; actual flavors vary with batch, roast and brewing and are not guaranteed.", "通常柔和甜润，带轻微果酸；实际风味随批次、烘焙及冲煮而变化。", "一般にまろやかな甘さと軽い果実の酸味が特徴です。風味はロット、焙煎、抽出により異なります。" },
            { "View recent order", "查看最近订单", "直近の注文を見る" },
            { "Recent order", "最近订单", "直近の注文" },
            { "Order confirmed", "订单已确认", "注文確定" },
            { "Your saved order. No new purchase has been made.", "这是已保存的订单，查看不会再次下单。", "保存済みの注文です。新しい注文は行われません。" },
            { "Tell me more about your preferences, or browse the menu.", "可以再说说你的偏好，或看看菜单。", "ご希望を詳しく教えていただくか、メニューをご覧ください。" },
            { "Preparing your drink... Please wait.", "正在制作咖啡，请稍等……", "コーヒーをお作りしています。少々お待ちください。" },
            { "Blue Mountain Black Coffee", "蓝山黑咖啡", "ブルーマウンテンコーヒー" },
            { "Geisha Black Coffee", "瑰夏黑咖啡", "ゲイシャコーヒー" },
            { "Kona Black Coffee", "科纳黑咖啡", "コナコーヒー" },
            { "Whole Milk", "全脂奶", "全乳" },
            { "Skim Milk", "脱脂奶", "無脂肪乳" },
            { "Oat Milk", "燕麦奶", "オーツミルク" },
            { "Coffee bean", "咖啡豆", "コーヒー豆" },
            { "Confirmed", "已确认", "確定済み" },
            { "Keep this number. You may take a seat.", "请保留号码，可以先找位置坐下。", "番号をお控えのうえ、お席でお待ちください。" },
            { "Talk to the barista to complete your choice. Confirm your drink only when you are ready.", "请先与店员完成选择，准备好后再确认下单。", "店員と相談して内容を選び、準備ができたら注文を確認してください。" },
            { "Check your connection in Virtual Cafe > LLM Settings.", "连接不可用，请检查服务设置后重试。", "接続できません。サービスの設定を確認してください。" },
            { "Close", "关闭", "閉じる" },
            { "Back", "返回", "戻る" },
            { "Continue", "继续", "次へ" },
            { "Click to reveal", "点击显示全文", "全文を表示" },
            { "YOUR BARISTA", "咖啡店员", "バリスタ" },
            { "YOU", "你", "あなた" },
            { "Choose your spoken language / 选择对话语言", "选择语言", "言語を選択" },
            { "Tap Speak to record; tap again to send. You can also type.", "点击“说话”开始录音，再次点击发送，也可以直接输入文字。", "「話す」で録音を開始し、もう一度押すと送信します。文字入力もできます。" },
            { "Would you like to choose a coffee yourself, or shall I help you find one?", "想自己选杯咖啡，还是让我帮你推荐？", "ご自分で選びますか？おすすめをご案内しましょうか？" },
            { "Hello! Welcome to our cafe. Would you like a coffee?", "欢迎光临！想喝杯咖啡吗？", "いらっしゃいませ！コーヒーはいかがですか？" },
            { "Hello! Tell me your taste, milk preference or budget.", "你好呀！告诉我你的口味、奶种偏好或预算吧。", "お好みの味、ミルク、ご予算を教えてください。" },
            { "Hello! What would you like? Tell me your taste, milk preference or budget.", "你好呀！告诉我你的口味、奶种偏好或预算吧。", "お好みの味、ミルク、ご予算を教えてください。" },
            { "How would you like to order?", "想怎样点单？", "ご注文方法をお選びください" },
            { "Would you like to order?", "要点单吗？", "ご注文なさいますか？" },
            { "Yes, please", "好呀，点一杯", "はい、お願いします" },
            { "Show me the menu", "看看菜单", "メニューを見る" },
            { "Help me choose", "帮我推荐", "おすすめを聞く" },
            { "Order by voice", "语音点单", "音声で注文" },
            { "Just browsing", "先逛逛", "見て回る" },
            { "Take your time. Which coffee would you like?", "慢慢选，今天想喝哪款咖啡？", "どうぞごゆっくり。どのコーヒーになさいますか？" },
            { "Choose your coffee", "选择咖啡", "コーヒーを選ぶ" },
            { "Good choice. Let's choose your cup size and ingredients.", "好选择！接下来选杯型和原料吧。", "いいですね！サイズと材料を選びましょう。" },
            { "Tell your barista", "告诉店员你的偏好", "お好みを教えてください" },
            { "Taste, milk preference, budget...", "口味、奶种偏好、预算……", "お好みの味、ミルク、ご予算など" },
            { "Send", "发送", "送信" },
            { "View menu", "查看菜单", "メニュー" },
            { "Of course. Here is our menu.", "当然，这是我们的菜单。", "もちろんです。こちらがメニューです。" },
            { "Retry", "重试", "再試行" },
            { "Speak", "说话", "話す" },
            { "Language", "语言", "言語" },
            { "Voice settings", "语音设置", "音声設定" },
            { "What would you like to do?", "接下来想怎么选？", "どうなさいますか？" },
            { "Choose this drink", "就选这杯", "この一杯にする" },
            { "Adjust size / ingredients", "调整杯型和原料", "サイズ・材料を変更" },
            { "Another recommendation", "换个推荐", "別のおすすめ" },
            { "Tell you more", "补充偏好", "希望を追加" },
            { "Make it your own", "定制你的咖啡", "お好みにカスタマイズ" },
            { "Review order", "核对订单", "注文を確認" },
            { "Change drink", "换款咖啡", "飲み物を変更" },
            { "Let's choose another coffee.", "那我们看看其他咖啡吧。", "別のコーヒーを選びましょう。" },
            { "Confirm your order", "确认订单", "ご注文の確認" },
            { "Confirm purchase", "确认购买", "注文を確定" },
            { "Go back and adjust", "返回修改", "戻って変更" },
            { "Send your preference before retrying.", "请先发送偏好，再重试。", "ご希望を送信してから再試行してください。" },
            { "Let me check our menu and the latest stock for you...", "我来看看菜单和最新库存，请稍等……", "メニューと最新の在庫を確認します。少々お待ちください…" },
            { "Preparing your drink...", "正在制作你的咖啡……", "コーヒーをお作りしています…" },
            { "Your drink is confirmed. Thank you! Keep this pickup number.", "订单已确认，谢谢！请保留取餐号。", "ご注文ありがとうございます。受取番号をお控えください。" },
            { "Take a seat and relax. Enjoy your cafe visit.", "可以先找个位置坐坐，享受咖啡时光。", "お席でごゆっくりお過ごしください。" },
            { "Order another", "再点一杯", "もう一杯注文" },
            { "Thanks, keep exploring", "谢谢，继续逛逛", "ありがとう、見て回る" },
            { "Complete your selection", "请完成选择", "選択を完了してください" },
            { "Back to options", "返回选择", "選択に戻る" },
            { "Order complete", "点单完成", "注文完了" },
            { "Cup size", "杯型", "サイズ" },
            { "Beans", "咖啡豆", "コーヒー豆" },
            { "Milk", "奶种", "ミルク" },
            { "No milk", "不加奶", "ミルクなし" },
            { "Select a cup size", "请选择杯型", "サイズを選択" },
            { "Select a coffee bean", "请选择咖啡豆", "コーヒー豆を選択" },
            { "Select milk", "请选择奶种", "ミルクを選択" },
            { "Selection incomplete", "选项未完成", "未選択の項目があります" },
            { "RECIPE / PER CUP", "每杯配方", "一杯分のレシピ" },
            { "Complete your size, bean and milk choices to see the recipe.", "选好杯型、咖啡豆及奶种后，即可查看配方。", "サイズ・豆・ミルクを選ぶとレシピが表示されます。" },
            { "Recipe details are unavailable for this selection.", "暂时无法获取该配置的配方。", "この組み合わせのレシピを取得できません。" },
            { "Blue Mountain coffee", "蓝山黑咖啡", "ブルーマウンテンコーヒー" },
            { "Geisha coffee", "瑰夏黑咖啡", "ゲイシャコーヒー" },
            { "Kona coffee", "科纳黑咖啡", "コナコーヒー" },
            { "Jamaica Blue Mountain", "牙买加蓝山", "ジャマイカ・ブルーマウンテン" },
            { "Panama Geisha", "巴拿马瑰夏", "パナマ・ゲイシャ" },
            { "Hawaii Kona", "夏威夷科纳", "ハワイ・コナ" },
            { "Blue Mountain", "蓝山", "ブルーマウンテン" },
            { "Geisha", "瑰夏", "ゲイシャ" },
            { "Kona", "科纳", "コナ" },
            { "Whole milk", "全脂奶", "全乳" },
            { "Skim milk", "脱脂奶", "無脂肪乳" },
            { "Oat milk", "燕麦奶", "オーツミルク" },
            { "Latte", "拿铁", "ラテ" },
            { "Available", "可制作", "注文可能" },
            { "Sold out", "售罄", "売り切れ" },
            { "Insufficient stock", "库存不足", "在庫不足" },
            { "Required", "需要", "必要量" },
            { "In stock", "现有", "在庫" },
            { "No stock was deducted.", "未扣减库存。", "在庫は減っていません。" },
            { "No available configuration", "暂无可制作的配置", "注文可能な組み合わせがありません" },
            { "Price unavailable", "暂无法获取价格", "価格を取得できません" },
            { "Choose size and ingredients", "选择杯型与原料", "サイズと材料を選択" },
            { ". Other available options can still be ordered.", "。仍可选择其他可售选项。", "。ほかの注文可能な項目をお選びください。" },
            { "Espresso yield", "萃取量", "抽出量" },
            { "Water", "水", "水" },
            { "Ice", "冰", "氷" },
            { "Please select: ", "请选择：", "選択してください：" },
            { "Your pickup number: ", "取餐号：", "受取番号：" },
            { "Total: CNY ", "合计：¥", "合計：CNY " },
            { "Mouse: select / Esc: close and walk", "鼠标：选择 / Esc：关闭并行走", "マウス：選択 / Esc：閉じて歩く" },
            { "E: stand up / Mouse: look around", "E：起身 / 鼠标：环顾", "E：立つ / マウス：見回す" },
            { "E: sit / WASD: move / Mouse: look", "E：坐下 / WASD：移动 / 鼠标：环顾", "E：座る / WASD：移動 / マウス：見回す" },
            { "WASD: move / Mouse: look / Approach the counter", "WASD：移动 / 鼠标：环顾 / 靠近柜台可交谈", "WASD：移動 / マウス：見回す / カウンターで会話" },
            { "F: talk to the assistant / WASD: move", "F：与店员交谈 / WASD：移动", "F：店員と話す / WASD：移動" },
            { "Not enough room to stand. Please try again.", "起身空间不足，请再试一次。", "立ち上がるスペースが足りません。" },
            { "Voice input is unavailable. You can still type.", "语音输入暂不可用，仍可输入文字。", "音声入力を利用できません。文字入力は可能です。" },
            { "Tap Speak to start recording.", "点击“说话”开始录音。", "「話す」を押すと録音します。" },
            { "Recording... Tap again to send.", "录音中，再次点击发送。", "録音中です。もう一度押すと送信します。" },
            { "Recognizing your speech...", "正在识别语音……", "音声を認識しています…" },
            { "Send / 结束并发送", "结束并发送", "録音終了・送信" },
            { "Speak / 说话", "说话", "話す" },
            { "Checking the latest menu. Please wait...", "正在查看最新菜单，请稍等……", "最新のメニューを確認中です…" },
            { "Unable to complete this action. Check your options, stock or connection and retry.", "操作未完成，请检查选项、库存或网络后重试。", "選択内容、在庫、接続を確認して再試行してください。" },
            { "No speech recognized. Please try again or type.", "未识别到语音，请重试或输入文字。", "音声を認識できませんでした。再試行または文字入力してください。" },
            { "Microphone permission denied. You can still type.", "麦克风权限未开启，仍可输入文字。", "マイクへのアクセスが許可されていません。文字入力できます。" },
            { "No microphone found. You can still type.", "未找到麦克风，仍可输入文字。", "マイクが見つかりません。文字入力できます。" },
            { "Cannot start microphone. You can still type.", "无法启动麦克风，仍可输入文字。", "マイクを起動できません。文字入力できます。" },
            { "Recording was empty or too short. Please try again.", "录音为空或太短，请重试。", "録音が空または短すぎます。再試行してください。" }
        };
        private static readonly Regex Tokens = BuildTokens();
        private static Regex BuildTokens()
        {
            var keys = new string[Phrases.GetLength(0)];
            for (int i = 0; i < keys.Length; i++) keys[i] = Phrases[i, 0];
            Array.Sort(keys, (a, b) => b.Length.CompareTo(a.Length));
            for (int i = 0; i < keys.Length; i++) keys[i] = Regex.Escape(keys[i]);
            return new Regex(string.Join("|", keys));
        }
        public static string Text(string text)
        {
            if (string.IsNullOrEmpty(text) || LanguageIndex == 1) return text;
            int column = LanguageIndex == 2 ? 2 : 1;
            return Tokens.Replace(text, match => {
                for (int i = 0; i < Phrases.GetLength(0); i++)
                    if (Phrases[i, 0] == match.Value) return Phrases[i, column];
                return match.Value;
            });
        }
        public static string Format(string english, string chinese, string japanese, params object[] args)
            => string.Format(LanguageIndex == 0 ? chinese : LanguageIndex == 2 ? japanese : english, args);
    }
}
