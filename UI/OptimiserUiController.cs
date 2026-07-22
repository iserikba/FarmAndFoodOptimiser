using System;
using Mafi;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiStatic.Toolbar;
using UnityEngine;
using Iserik.FaFOptimiser.Translations;

namespace Iserik.FaFOptimiser.UI
{
    // RegistrationMode.Normal ensures this controller is loaded during standard gameplay
    [GlobalDependency(RegistrationMode.AsEverything, false, false)]
    public sealed class OptimiserUiController : WindowController<OptimiserMainWindow>, IToolbarItemController, IUnityInputController
    {
        // --- UPDATED: Pointing to your custom SVG instead of the game's default ---
        //private const string ToolbarIconPath = "Assets/Unity/UserInterface/Toolbar/Stats.svg";
        // Revert back to the constant string. 
        // The game will use this to find the file, and now that the SVG is safe, it will render properly!
        private const string ToolbarIconPath = "Assets/FaFoptimiser/target1.png";

        private const float ToolbarOrder = 905f; 

        // Determines if the button shows up on the bottom toolbar
        public bool IsVisible => true;

        // Ensures the shortcut only works when the game is in a state where UI can be opened
        public bool DeactivateShortcutsIfNotVisible => true; 

        // Required by the IToolbarItemController interface contract
        public event Action<IToolbarItemController> VisibilityChanged;  

        public OptimiserUiController(ControllerContext controllerContext, ToolbarHud toolbar)
            : base(controllerContext, null)
        {
            // Register the toolbar button and the F8 shortcut key natively
            toolbar.AddMainMenuButton(
                Strings.WindowTitle,
                this,
                ToolbarIconPath,
                ToolbarOrder,
                (ShortcutsManager sm) => KeyBindings.FromKey(KbCategory.Tools, ShortcutMode.Game, KeyCode.F11)
            );

            Log.Info("FaFOptimiser: Toolbar button and F11 shortcut registered.");
        }

        // Optional: If you need anything to happen exactly when the window opens
        protected override void OnActivate()
        {
            base.OnActivate();
            // E.g., base.Window.RefreshData();
        }
    }
}