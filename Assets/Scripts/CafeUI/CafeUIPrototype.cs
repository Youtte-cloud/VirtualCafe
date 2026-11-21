using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VirtualCafe.Inventory;

namespace VirtualCafe.UI
{
    // D UI uses B shared menu/inventory. Assistant remains an explicitly labelled mock.
    public class CafeUIPrototype : MonoBehaviour
    {
        public event Action<string> ProductSelected;
        CafeMenuService menu;
        MenuSelection selected, previous;
        readonly List<string> trace = new List<string>();
        Font font; RectTransform content; Text status, transcript, orderText, debugText;
        InputField input; Button send, confirm; int quantity=1; bool busy, purchasing;
        string page="Home", conversation="MOCK MODE: no LLM service is connected.\nDescribe a preference (English), or type /fail to test an error.";
        int requestVersion;
        Sprite rounded;
        GameObject debugPanel;
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        static readonly Color Ink = new Color(.23f,.18f,.15f);
        static readonly Color Muted = new Color(.48f,.42f,.36f);
        static readonly Color Cream = new Color(.98f,.96f,.92f);
        static readonly Color Coffee = new Color(.36f,.23f,.17f);
        static readonly Color Sage = new Color(.83f,.87f,.79f);
        Sprite RoundedSprite()
        {
            if(rounded!=null)return rounded;
            const int size=64;const float radius=14;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=Mathf.Max(radius-x,0,x-(size-1-radius));
                float dy=Mathf.Max(radius-y,0,y-(size-1-radius));
                float a=Mathf.Clamp01(radius-Mathf.Sqrt(dx*dx+dy*dy)+.5f);
                texture.SetPixel(x,y,new Color(1,1,1,a));
            }
            texture.Apply();
            rounded=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(16,16,16,16));
            return rounded;
        }


        // Legacy MOCK UI is opt-in only. Add this component explicitly in a test
        // scene if needed; never spawn it automatically in the shared CafeMain.
        void Awake()
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            menu = CafeRuntimeServices.Menu; Build(); Show("Home");
            menu.InventoryChanged += OnStockChanged;
        }
        void OnDestroy() { if(menu!=null) menu.InventoryChanged -= OnStockChanged; }
        void OnStockChanged()
        {
            Log("Shared ingredient inventory changed; refresh current view.");
            if(!purchasing) Show(page);
        }
        static MenuSelection Copy(MenuSelection x) => x==null?null:new MenuSelection {
            MenuItemId=x.MenuItemId, CupSizeMl=x.CupSizeMl, BeanId=x.BeanId, MilkId=x.MilkId, Servings=x.Servings };
        static string Money(int cents) => "CNY " + (cents / 100m).ToString("0.00");
        string ItemName(string id)
        {
            switch(id) { case "latte": return "Latte"; case "black-geisha": return "Geisha black coffee";
                case "black-kona": return "Kona black coffee"; default:return "Blue Mountain black coffee"; }
        }
        string Describe(MenuSelection x) => ItemName(x.MenuItemId)+" / "+x.CupSizeMl+"ml";
        void CancelRequest()
        {
            if(!busy)return;
            requestVersion++; StopAllCoroutines(); busy=false;
            conversation="MOCK request cancelled when leaving chat. No order was changed.";
            Log("Cancelled obsolete mock request");
        }
        void Build()
        {
            if (FindObjectOfType<EventSystem>()==null)
                new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            var canvas=new GameObject("Cafe Prototype Canvas",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform,false);
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder=100;
            var scale=canvas.GetComponent<CanvasScaler>(); scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution=new Vector2(1280,720); scale.matchWidthOrHeight=.5f;
            var shell=Box(canvas.transform,"Cafe front desk",Cream);
            shell.anchorMin=new Vector2(0,0);shell.anchorMax=new Vector2(.49f,1);
            shell.offsetMin=new Vector2(24,24);shell.offsetMax=new Vector2(-12,-24);
            var shadow=shell.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(.12f,.07f,.03f,.22f);shadow.effectDistance=new Vector2(0,-5);
            var layout=shell.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding=new RectOffset(28,28,24,20);layout.spacing=10;layout.childControlHeight=true;layout.childForceExpandHeight=false;
            var eyebrow=Label(shell,"V I R T U A L   C A F E",12,20);eyebrow.color=Muted;
            Label(shell,"A little coffee break.",30,42).fontStyle=FontStyle.Bold;
            Label(shell,"Good coffee. Your way.",15,24).color=Muted;
            var nav=Row(shell,40);
            navigation["Home"]=Button(nav,"Welcome",()=>Show("Home"));
            navigation["Menu"]=Button(nav,"Our menu",()=>Show("Menu"));
            navigation["Chat"]=Button(nav,"Assistant",()=>Show("Chat"));
            status=Label(shell,"Choose something lovely from our menu.",13,36);status.color=Muted;
            // Scrollable content keeps latte controls accessible on smaller game views.
            var scrollRoot=Box(shell,"Content scroll",Color.clear);
            scrollRoot.gameObject.AddComponent<LayoutElement>().flexibleHeight=1;
            var scroll=scrollRoot.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            var viewport=Box(scrollRoot,"Viewport",Color.clear);Stretch(viewport,0);viewport.gameObject.AddComponent<RectMask2D>();
            content=Box(viewport,"Page",Color.clear);
            content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var vertical=content.gameObject.AddComponent<VerticalLayoutGroup>();vertical.spacing=12;vertical.padding=new RectOffset(0,0,0,8);vertical.childControlHeight=true;vertical.childForceExpandHeight=false;
            var fitter=content.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=viewport;scroll.content=content;
            // Visible rail makes overflow discoverable; keep it separate from the clipped viewport.
            viewport.offsetMax=new Vector2(-18,0);
            var rail=Box(scrollRoot,"Vertical scrollbar",new Color(.87f,.83f,.77f));
            rail.anchorMin=new Vector2(1,0);rail.anchorMax=Vector2.one;
            rail.pivot=new Vector2(1,.5f);rail.sizeDelta=new Vector2(8,0);rail.anchoredPosition=Vector2.zero;
            var slide=Box(rail,"Sliding area",Color.clear);Stretch(slide,1);
            var handle=Box(slide,"Handle",new Color(.55f,.43f,.33f));Stretch(handle,0);
            var bar=rail.gameObject.AddComponent<Scrollbar>();
            bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();
            bar.direction=Scrollbar.Direction.BottomToTop;
            bar.navigation=new Navigation { mode=Navigation.Mode.None };
            var barColors=bar.colors;barColors.highlightedColor=new Color(.88f,.83f,.75f);
            barColors.pressedColor=new Color(.73f,.66f,.56f);bar.colors=barColors;
            scroll.verticalScrollbar=bar;
            scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            scroll.scrollSensitivity=24;

            var footer=Row(shell,32);
            Button(footer,"Start over",ResetDemo,32);
            Button(footer,"Debug trace",()=>debugPanel.SetActive(!debugPanel.activeSelf),32);
            Label(shell,"Assistant preview / simulated replies",11,18).color=Muted;

            var badge=Box(canvas.transform,"Scene caption",new Color(.98f,.96f,.92f,.94f));
            badge.anchorMin=new Vector2(.53f,.84f);badge.anchorMax=new Vector2(.95f,.96f);badge.offsetMin=Vector2.zero;badge.offsetMax=Vector2.zero;
            var caption=Label(badge,"THE CAFE\nTake a moment. Make yourself at home.",18,70);Stretch(caption.rectTransform,18);caption.fontStyle=FontStyle.Bold;
            var debug=Box(canvas.transform,"Debug trace",new Color(.16f,.20f,.17f,.97f));
            debug.anchorMin=new Vector2(.53f,.04f);debug.anchorMax=new Vector2(.97f,.65f);debug.offsetMin=Vector2.zero;debug.offsetMax=Vector2.zero;
            debugPanel=debug.gameObject;debugText=Label(debug,"",13,360);debugText.color=Cream;Stretch(debugText.rectTransform,20);
            debugPanel.SetActive(false);
            Log("Shared inventory connected. Assistant is a mock; no API calls.");

        }
        void Show(string target)
        {
            if(page=="Chat" && target!="Chat") CancelRequest();
            page=target;
            foreach(var entry in navigation)
            {
                bool active=entry.Key==target || (target=="Order" && entry.Key=="Menu");
                entry.Value.GetComponent<Image>().color=active?Coffee:new Color(.91f,.88f,.82f);
                entry.Value.GetComponentInChildren<Text>().color=active?Cream:Ink;
            }
            content.anchoredPosition=Vector2.zero;
            foreach(Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            transcript=null; input=null; send=null; confirm=null; orderText=null;
            if(target=="Home")
            {
                Label(content,"Find your everyday favourite.",25,66).fontStyle=FontStyle.Bold;
                Label(content,"Explore our coffees, choose your cup,\nand make it your own.",18,90);
                Button(content,"Explore the menu  >",()=>Show("Menu"),54);
                Button(content,"Help me choose  >",()=>Show("Chat"),54);
            }
            else if(target=="Menu")
            {
                Label(content,"Freshly selected, just for you.",23,36);
                foreach(var item in menu.GetMenu().items)
                {
                    var id=item.id;
                    var offers=Array.FindAll(menu.GetMenuOffers(),o=>o.selection.MenuItemId==id);
                    bool available=Array.Exists(offers,o=>o.available);
                    int lowest=int.MaxValue;foreach(var offer in offers)lowest=Math.Min(lowest,offer.price.unitPriceCents);
                    string note=id=="latte"?"Your choice of bean & milk":"Single-origin / black coffee";
                    var b=Button(content,ItemName(id)+"    /    from "+Money(lowest)+"\n"+note+(available?"":"  /  SOLD OUT"),()=>Select(id),76);
                    b.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleLeft;
                    b.interactable=available;

                }

            }
            else if(target=="Chat")
            {
                Label(content,"What are you in the mood for?",23,36);
                var bubble=Box(content,"Conversation",new Color(.92f,.93f,.87f));
                bubble.gameObject.AddComponent<LayoutElement>().preferredHeight=220;
                transcript=Label(bubble,conversation,16,220);Stretch(transcript.rectTransform,18);
                input=MakeInput(content,"Try: latte / black / oat / same / no caffeine /fail");
                send=Button(content,busy?"Waiting...":"Send message  >",Send,42); send.interactable=!busy;
                if(selected!=null) Button(content,"Review: "+Describe(selected),()=>Show("Order"),42);
            }
            else if(target=="Order")
            {
                Label(content,"Make it yours.",23,36);
                orderText=Label(content,"",20,100); UpdateOrder();
                var options=menu.GetMenu().items;
                var definition=Array.Find(options,i=>i.id==selected.MenuItemId);
                Label(content,"CUP SIZE",11,18).color=Muted;
                var row=Row(content,38);
                foreach(var size in definition.cupSizesMl) { var value=size; Button(row,size+"ml",()=>{selected.CupSizeMl=value;UpdateOrder();}); }
                if(definition.milkIds.Length>0)
                {
                    Label(content,"COFFEE BEAN",11,18).color=Muted;
                    var beans=Row(content,38);
                    foreach(var bean in definition.beanIds) { var value=bean;Button(beans,bean.Replace("bean-",""),()=>{selected.BeanId=value;UpdateOrder();}); }
                    Label(content,"MILK",11,18).color=Muted;
                    var milks=Row(content,38);
                    foreach(var milk in definition.milkIds) { var value=milk;Button(milks,milk.Replace("milk-",""),()=>{selected.MilkId=value;UpdateOrder();}); }
                }
                Label(content,"QUANTITY",11,18).color=Muted;
                var qty=Row(content,38);
                Button(qty,"-",()=>{quantity=Math.Max(1,quantity-1);UpdateOrder();});
                Button(qty,"+",()=>{quantity=Math.Min(99,quantity+1);UpdateOrder();});
                confirm=Button(content,"Confirm order  >",Purchase,44);
                UpdateOrder();
                Button(content,"Back to menu",()=>{selected=null;Show("Menu");},40);
            }
        }
        void Select(string id)
        {
            CancelRequest();
            var offers=menu.GetMenuOffers();
            var offer=Array.Find(offers,o=>o.selection.MenuItemId==id && o.available);
            if(offer==null) {status.text="Sold out.";return;}
            selected=Copy(offer.selection);quantity=1;
            ProductSelected?.Invoke(selected.MenuItemId);Log("Pending selection: "+id+"; no deduction");Show("Order");
        }
        void UpdateOrder()
        {
            if(selected==null)return;
            selected.Servings=quantity;
            bool priced=menu.TryGetPrice(selected,out var quote,out string reason);
            bool available=priced && menu.IsAvailable(selected,out reason);
            if(orderText!=null)orderText.text=Describe(selected)+" x "+quantity+"\n"+
                (selected.MenuItemId=="latte"?selected.BeanId.Replace("bean-","")+" / "+selected.MilkId.Replace("milk-","")+"\n":"")+
                (priced?Money(quote.totalPriceCents):"Price unavailable")+" / "+(available?"Available":"Unavailable");
            if(confirm!=null)confirm.interactable=available&&!purchasing;
            status.text=available?"Pending only. Confirm to deduct shared ingredients.":reason;
        }
        void Purchase()
        {
            if(purchasing||selected==null)return;
            purchasing=true;if(confirm!=null)confirm.interactable=false;
            var purchase=Copy(selected);
            try
            {
                if(menu.TryConfirmOrder(new[]{purchase},out string reason))
                {
                    previous=Copy(purchase);selected=null;quantity=1;
                    Log("CONFIRMED: "+Describe(purchase)+" x "+purchase.Servings+"; shared stock saved");
                    Show("Menu");status.text="Purchase confirmed. Ingredient stock saved.";
                }
                else { UpdateOrder();status.text="Purchase failed: "+reason;Log("REJECTED: "+reason); }
            }
            catch(Exception e) {status.text="Purchase failed: "+e.Message;Log("Purchase error: "+e.Message);}
            finally {purchasing=false;if(page=="Order" && confirm!=null) confirm.interactable=selected!=null && menu.IsAvailable(selected,out _);}
        }
        void Send()
        {
            if(busy||input==null)return;
            string message=input.text.Trim(); if(message.Length==0){status.text="Please enter a message.";return;}
            busy=true;send.interactable=false;input.text="";
            conversation="You: "+message+"\n\nMOCK assistant: waiting...";transcript.text=conversation;
            Log("MOCK request: "+message+"; latest shared offers="+menu.GetMenuOffers().Length);
            StartCoroutine(MockReply(message,requestVersion));
        }
        IEnumerator MockReply(string message,int version)
        {
            yield return new WaitForSeconds(.8f);
            if(version!=requestVersion)yield break;
            busy=false;string lower=message.ToLowerInvariant();
            if(lower.Contains("/fail"))
            {
                conversation="You: "+message+"\n\nMOCK ERROR: request failed. Retry or use Menu. No order or stock was changed.";
                status.text="Simulated failure; direct ordering remains available.";Log("MOCK FAILURE: no mutation");
            }
            else
            {
                MenuSelection choice=null;
                var offers=menu.GetMenuOffers();
                bool caffeineFree=lower.Contains("no caffeine")||lower.Contains("caffeine-free")||lower.Contains("without caffeine");
                if(!caffeineFree)
                {
                    if(lower.Contains("same")&&previous!=null && menu.IsAvailable(previous,out _)) choice=Copy(previous);
                    if(choice==null)
                    {
                        bool black=lower.Contains("black")||lower.Contains("bitter")||lower.Contains("no milk");
                        bool oat=lower.Contains("oat");
                        var offer=Array.Find(offers,o=>o.available && (black?o.selection.MenuItemId!="latte":o.selection.MenuItemId=="latte") &&
                            (!oat||o.selection.MilkId=="milk-oat"));
                        if(offer!=null)choice=Copy(offer.selection);
                    }
                }
                // The request is cancelled on navigation. Never overwrite an order from another page.
                if(page!="Chat")yield break;
                selected=choice;quantity=1;if(selected!=null)selected.Servings=1;
                if(choice!=null)
                {
                    ProductSelected?.Invoke(choice.MenuItemId);
                    conversation="You: "+message+"\n\nMOCK assistant: Suggested "+Describe(choice)+". Review options before confirming.\nFixed simulation, not an LLM response.";
                    Log("MOCK OUTPUT: "+choice.MenuItemId+"; pending only");
                }
                else
                {
                    conversation="You: "+message+"\n\nMOCK assistant: "+(caffeineFree?"The current menu has no caffeine-free drink.":"No available matching combination.")+" Change preferences or browse Menu.";
                    Log("MOCK OUTPUT: no valid available match");
                }
                status.text="Mock reply received. Stock changes only after purchase.";
            }
            if(page=="Chat")Show("Chat");
        }
        void ResetDemo()
        {
            requestVersion++;StopAllCoroutines();busy=false;purchasing=false;selected=null;previous=null;quantity=1;
            conversation="MOCK MODE: demo reset. Try latte, black, oat, same, no caffeine, or /fail.";
            status.text="UI reset. Saved ingredient stock is unchanged.";Log("RESET conversation and pending order only; saved stock unchanged");Show("Home");
        }
        void Log(string value)
        {
            trace.Add(value);if(trace.Count>7)trace.RemoveAt(0);
            if(debugText!=null)debugText.text="LOCAL DEBUG / NOT REAL LLM EVIDENCE\n\n"+string.Join("\n\n",trace);
        }
        static void Stretch(RectTransform rect,float padding)
        {
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(padding,padding);rect.offsetMax=new Vector2(-padding,-padding);
        }
        RectTransform Box(Transform parent,string name,Color color)
        {
            var g=new GameObject(name,typeof(RectTransform),typeof(Image));g.transform.SetParent(parent,false);var image=g.GetComponent<Image>();image.color=color;image.sprite=RoundedSprite();image.type=Image.Type.Sliced;return g.GetComponent<RectTransform>();
        }
        Text Label(Transform parent,string value,int size,float height)
        {
            var g=new GameObject("Text",typeof(RectTransform),typeof(Text),typeof(LayoutElement));g.transform.SetParent(parent,false);
            var t=g.GetComponent<Text>();t.font=font;t.text=value;t.fontSize=size;t.lineSpacing=1.15f;t.color=Ink;t.supportRichText=false;t.alignment=TextAnchor.UpperLeft;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;
            g.GetComponent<LayoutElement>().preferredHeight=height;return t;
        }
        Button Button(Transform parent,string title,UnityEngine.Events.UnityAction action,float height=42)
        {
            var r=Box(parent,title,new Color(.91f,.88f,.82f));r.gameObject.AddComponent<LayoutElement>().preferredHeight=height;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=r.GetComponent<Image>();b.onClick.AddListener(action);
            var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.93f,.91f,.86f);colors.pressedColor=new Color(.78f,.75f,.68f);colors.selectedColor=Color.white;colors.disabledColor=new Color(.72f,.70f,.66f,.5f);colors.fadeDuration=.12f;b.colors=colors;
            b.navigation=new Navigation { mode=Navigation.Mode.None };
            var t=Label(r,title,17,height);t.alignment=TextAnchor.MiddleCenter;t.fontStyle=FontStyle.Bold;t.rectTransform.anchorMin=Vector2.zero;t.rectTransform.anchorMax=Vector2.one;t.rectTransform.offsetMin=new Vector2(16,0);t.rectTransform.offsetMax=new Vector2(-16,0);return b;
        }
        RectTransform Row(Transform parent,float height)
        {
            var r=Box(parent,"Row",Color.clear);r.gameObject.AddComponent<LayoutElement>().preferredHeight=height;
            var l=r.gameObject.AddComponent<HorizontalLayoutGroup>();l.spacing=8;l.childControlWidth=true;l.childForceExpandWidth=true;return r;
        }
        InputField MakeInput(Transform parent,string placeholder)
        {
            var r=Box(parent,"Message Input",new Color(1f,.995f,.98f));r.gameObject.AddComponent<LayoutElement>().preferredHeight=52;
            var field=r.gameObject.AddComponent<InputField>();var t=Label(r,"",17,52);t.color=Ink;t.alignment=TextAnchor.MiddleLeft;
            t.rectTransform.anchorMin=Vector2.zero;t.rectTransform.anchorMax=Vector2.one;t.rectTransform.offsetMin=new Vector2(10,0);t.rectTransform.offsetMax=new Vector2(-10,0);
            var hint=Label(r,placeholder,14,52);hint.color=Muted;hint.alignment=TextAnchor.MiddleLeft;hint.rectTransform.anchorMin=Vector2.zero;hint.rectTransform.anchorMax=Vector2.one;hint.rectTransform.offsetMin=new Vector2(10,0);hint.rectTransform.offsetMax=new Vector2(-10,0);
            field.textComponent=t;field.placeholder=hint;field.characterLimit=220;field.lineType=InputField.LineType.SingleLine;return field;
        }
    }
}
