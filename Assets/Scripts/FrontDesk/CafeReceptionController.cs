using UnityEngine;
using UnityEngine.Events;
using VirtualCafe.Inventory;

public sealed class CafeReceptionController : MonoBehaviour
{
    public GameObject welcomePanel;
    public GameObject orderPanel;
    public GameObject recommendationPanel;
    public CafeMenuSceneAdapter menuAdapter;
    public CafeProductHighlighter highlighter;

    // 供组员连接菜单和AI模块。
    public UnityEvent OrderOpened = new UnityEvent();
    public UnityEvent RecommendationOpened = new UnityEvent();
    public UnityEvent ReceptionEnded = new UnityEvent();

    private void Start()
    {
        BeginReception();
    }

    private void HidePanels()
    {
        if (welcomePanel) welcomePanel.SetActive(false);
        if (orderPanel) orderPanel.SetActive(false);
        if (recommendationPanel) recommendationPanel.SetActive(false);
    }

    public void BeginReception()
    {
        HidePanels();
        if (highlighter) highlighter.ClearHighlight();
        if (welcomePanel) welcomePanel.SetActive(true);
    }

    public void OpenOrder()
    {
        HidePanels();
        if (highlighter) highlighter.ClearHighlight();
        if (orderPanel) orderPanel.SetActive(true);

        // 只查询报价和库存，不扣库存。
        if (menuAdapter) menuAdapter.RefreshDisplay();

        OrderOpened.Invoke();
    }

    public void OpenRecommendation()
    {
        HidePanels();
        if (highlighter) highlighter.ClearHighlight();
        if (recommendationPanel) recommendationPanel.SetActive(true);

        RecommendationOpened.Invoke();
    }

    public void EndReception()
    {
        HidePanels();
        if (highlighter) highlighter.ClearHighlight();
        ReceptionEnded.Invoke();
    }

    private void OnDestroy()
    {
        if (highlighter) highlighter.ClearHighlight();
    }
}