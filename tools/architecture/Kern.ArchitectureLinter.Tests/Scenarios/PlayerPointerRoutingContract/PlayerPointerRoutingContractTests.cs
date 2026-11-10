#nullable enable

using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class PlayerPointerRoutingContractTests
{
    [Test]
    public void WorldClickRoutingUsesUnifiedPointerAndKeepsUiDownGuard()
    {
        string projectRoot = FindProjectRoot();
        string interactionPath = Path.Combine(
            projectRoot,
            "Assets/Scripts/Game/Player/Actions/Control/PlayerInteractionController.cs");
        string hudPath = Path.Combine(
            projectRoot,
            "Assets/Scripts/UI/HUD/Player/View/PlayerHUDView.cs");
        string touchControlsPath = Path.Combine(
            projectRoot,
            "Assets/Scripts/UI/HUD/Player/Touch/PlayerHUDTouchControls.cs");
        string interactionSource = File.ReadAllText(interactionPath);
        string hudSource = File.ReadAllText(hudPath);
        string touchControlsSource = File.ReadAllText(touchControlsPath);

        Assert.That(interactionSource, Does.Contain("Pointer? pointer = Pointer.current;"));
        Assert.That(interactionSource, Does.Contain("pointer.press.wasPressedThisFrame"));
        Assert.That(interactionSource, Does.Contain("pointer.position.ReadValue()"));
        Assert.That(interactionSource, Does.Contain("private void HandlePointerClick()"));
        Assert.That(interactionSource, Does.Contain("_uiPointerDownFrame == _pendingClickFrame"));
        Assert.That(interactionSource, Does.Not.Contain("Mouse.current == null"));
        Assert.That(interactionSource, Does.Not.Contain("Touchscreen.current"));
        Assert.That(hudSource, Does.Contain("InitializeTouchControls(tree);"));
        Assert.That(touchControlsSource, Does.Contain("private void OnMovePointerDown(PointerDownEvent evt)"));
        Assert.That(touchControlsSource, Does.Contain("_movePad.CapturePointer(evt.pointerId);"));
        Assert.That(touchControlsSource, Does.Contain("evt.StopPropagation();"));
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "Assets/Scripts/Game/Player/Actions/Control/PlayerInteractionController.cs");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Unity project root.");
    }
}
