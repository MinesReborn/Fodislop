#nullable enable

using Kern.Game.Player.Stats;
using Kern.UI.HUD;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Editor;

// Данные стенда макета (visual/kern-ui, ?state=busy) для снимка HUD: панели заполняет рабочий код HUD
// (PlayerHUDStatsPresenter, корзина, состояния, рост навыков, миссия) из настоящей модели статов игрока.
// Миникарта — свой документ в игре; на снимке её разметка встаёт в то же дерево, чтобы сверить место.
internal static class HudSnapshotFill
{
    public static void Apply(VisualElement tree)
    {
        var stats = new PlayerStatsModel();
        stats.SetNickname("ШАХТЁР-774");
        stats.SetPlayerId(10482);
        stats.SetLevel(14);
        stats.SetHealth(113, 136);
        stats.SetCurrency(14850, 340);
        stats.SetBasket(1800, new long[] { 2340, 1204, 96, 12, 3, 57 });
        stats.SetGeology(3, 10, default, "Гипноскал");
        stats.AddStatusLine("mining", new[] { "Добыча x4", string.Empty }, new Color(1f, 0.647f, 0f), 0, 0);
        stats.AddStatusLine("xp", new[] { "Прокачка x3", string.Empty }, new Color(0f, 0.784f, 0f), 0, 0);
        stats.SetMission("Первая добыча", "Накопай 10 синих кристаллов", 10);
        stats.SetMissionProgress(3);
        stats.SetOnline(38, 7);

        var basket = new PlayerHUDBasketView();
        basket.Initialize(Require(tree, "BasketContainer"));
        var presenter = new PlayerHUDStatsPresenter(stats, basket, tree);
        presenter.Refresh();

        var status = new PlayerHUDStatusPanel();
        status.Initialize(tree);
        status.Rebuild(stats);

        var skills = new PlayerHUDSkillGrid();
        skills.Initialize(Require(tree, "SkillContainer"));
        skills.UpdateSkillProgress(SkillType.Movement, 100, 100);

        var mission = new PlayerHUDMissionPanel(null!, null!, null!, null!);
        mission.Initialize(tree);
        mission.Update(stats);

        VisualTreeAsset? minimap = Resources.Load<VisualTreeAsset>("UI/Map/Minimap/Templates/Minimap");
        if (minimap != null)
        {
            TemplateContainer map = minimap.Instantiate();
            map.AddToClassList("ui-fullscreen");
            map.pickingMode = PickingMode.Ignore;
            if (map.Q<Label>("MinimapCoordinates") is { } coords)
            {
                coords.text = "1184:902";
            }

            tree.Add(map);
        }
    }

    private static VisualElement Require(VisualElement tree, string name) =>
        tree.Q<VisualElement>(name) ?? throw new System.InvalidOperationException($"[HudSnapshot] {name} is missing from PlayerHUD.uxml.");
}
