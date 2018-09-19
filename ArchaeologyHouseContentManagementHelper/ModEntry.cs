﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StardewMods.ArchaeologyHouseContentManagementHelper.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using Harmony;
using StardewValley.Menus;

namespace StardewMods.ArchaeologyHouseContentManagementHelper
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        private MuseumInteractionDialogService dialogService;

        public static CommonServices CommonServices { get; private set; }

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            if (helper == null)
            {
                Monitor.Log("Error: [modHelper] cannot be [null]!", LogLevel.Error);
                throw new ArgumentNullException(nameof(helper), "Error: [modHelper] cannot be [null]!");
            }

            CommonServices = new CommonServices(Monitor, helper.Translation, helper.Reflection);

            SaveEvents.AfterLoad += Bootstrap;
        }

        private void Bootstrap(object sender, EventArgs e)
        {
            dialogService = new MuseumInteractionDialogService();

            InputEvents.ButtonPressed += InputEvents_ButtonPressed;

            LostBookFoundDialogExtended.Setup();

            var harmony = HarmonyInstance.Create("StardewMods.ArchaeologyHouseContentManagementHelper");
            Patches.Patch.PatchAll(harmony);
        }


        /// <summary>The method invoked when the player presses a controller, keyboard, or mouse button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void InputEvents_ButtonPressed(object sender, EventArgsInput e)
        {    
            if (e.IsActionButton && Context.IsPlayerFree && LibraryMuseumHelper.IsPlayerAtCounter(Game1.player))
            {
                LibraryMuseum museum = Game1.currentLocation as LibraryMuseum;
                bool canDonate = museum.doesFarmerHaveAnythingToDonate(Game1.player);

                int donatedItems = LibraryMuseumHelper.MuseumPieces;
            
                if (canDonate)
                {
                    if (donatedItems > 0)
                    {
                        // Can donate, rearrange museum and collect rewards
                        if (LibraryMuseumHelper.HasPlayerCollectibleRewards(Game1.player))
                        {
                            dialogService.ShowDialog(MuseumInteractionDialogType.DonateRearrangeCollect);
                        }

                        // Can donate and rearrange museum
                        else
                        {
                            dialogService.ShowDialog(MuseumInteractionDialogType.DonateRearrange);
                        }                        
                    }

                    // Can donate & collect rewards & no item donated yet (cannot rearrange museum)
                    else if (LibraryMuseumHelper.HasPlayerCollectibleRewards(Game1.player))
                    {
                        dialogService.ShowDialog(MuseumInteractionDialogType.DonateCollect);
                    }

                    // Can donate & no item donated yet (cannot rearrange)
                    else
                    {
                        dialogService.ShowDialog(MuseumInteractionDialogType.Donate);
                    }
                }

                // No item to donate, donated at least one item and can potentially collect a reward
                else if (donatedItems > 0)
                {
                    // Can rearrange and collect a reward
                    if (LibraryMuseumHelper.HasPlayerCollectibleRewards(Game1.player))
                    {
                        dialogService.ShowDialog(MuseumInteractionDialogType.RearrangeCollect);
                    }

                    // Can rearrange and no rewards available
                    else
                    {
                        dialogService.ShowDialog(MuseumInteractionDialogType.Rearrange);
                    }                    
                }

                else
                {
                    // Show original game message. Currently in the following cases:
                    //  - When no item has been donated yet
                }
            }
        }
    }
}
