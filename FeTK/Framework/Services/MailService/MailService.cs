﻿using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using FelixDev.StardewMods.FeTK.ModHelpers;
using StardewValley;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FelixDev.StardewMods.FeTK.Framework.Helpers;
using FelixDev.StardewMods.FeTK.Framework.Serialization;

namespace FelixDev.StardewMods.FeTK.Framework.Services
{
    /// <summary>
    /// Provides an API to add mails to the player's mailbox.
    /// </summary>
    public class MailService : IMailSender
    {
        /// <summary>The prefix of the key used to identify the save data created by this mail service.</summary>
        private const string SAVE_DATA_KEY_PREFIX = "FelixDev.StardewMods.FeTK.Framework.Services.MailService";

        /// <summary>Provides access to the <see cref="IModEvents"/> API provided by SMAPI.</summary>
        private static readonly IModEvents events = ToolkitMod.ModHelper.Events;

        /// <summary>Provides access to the <see cref="IMonitor"/> API provided by SMAPI.</summary>
        private static readonly IMonitor monitor = ToolkitMod._Monitor;

        /// <summary>The ID of the mod which uses this mail service.</summary>
        private readonly string modId;

        /// <summary>The key used to identify the save data created by this mail service.</summary>
        private readonly string saveDataKey;

        /// <summary>The mail manager used to add mails to the game and provide mail events.</summary>
        private readonly IMailManager mailManager;

        /// <summary>The save data manager for this mail service.</summary>
        private readonly ModSaveDataHelper saveDataHelper;

        /// <summary>A helper to write and retrieve the save data for this mail service.</summary>
        private readonly SaveDataBuilder saveDataBuilder;

        /// <summary>
        /// Contains all mails added with this mail service which have not been read by the player yet. 
        /// For each day a collection of mails with that arrival day is stored (using a mapping [mail ID] -> [mail]).
        /// </summary>
        private Dictionary<int, Dictionary<string, Mail>> timedMails = new Dictionary<int, Dictionary<string, Mail>>();

        /// <summary>
        /// Raised when a mail begins to open. The mail content can still be changed at this point.
        /// </summary>
        public event EventHandler<MailOpeningEventArgs> MailOpening;

        /// <summary>
        /// Raised when a mail has been closed.
        /// </summary>
        public event EventHandler<MailClosedEventArgs> MailClosed;

        /// <summary>
        /// Create a new instance of the <see cref="MailService"/> class.
        /// </summary>
        /// <param name="modId">The ID of the mod for which this mail service will be created for.</param>
        /// <param name="mailManager">The <see cref="IMailManager"/> instance which will be used by this service to add mails to the game.</param>
        /// <exception cref="ArgumentNullException">
        /// The specified <paramref name="modId"/> is <c>null</c> or does not contain at least one 
        /// non-whitespace character.</exception>
        /// <exception cref="ArgumentNullException">The specified <paramref name="mailManager"/> is <c>null</c>.</exception>
        internal MailService(string modId, IMailManager mailManager)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("The mod ID needs to contain at least one non-whitespace character!", nameof(modId));
            }

            this.modId = modId;
            this.mailManager = mailManager ?? throw new ArgumentNullException(nameof(mailManager));

            this.saveDataKey = SAVE_DATA_KEY_PREFIX + "." + modId;

            this.saveDataHelper = ModSaveDataHelper.GetSaveDataHelper(modId);
            this.saveDataBuilder = new SaveDataBuilder();

            events.GameLoop.Saving += OnSaving;
            events.GameLoop.SaveLoaded += OnSaveLoaded;
        }

        /// <summary>
        /// Add a mail to the player's mailbox.
        /// </summary>
        /// <param name="daysFromNow">The day offset when the mail will arrive in the mailbox.</param>
        /// <param name="mail">The mail to add.</param>
        /// <exception cref="ArgumentOutOfRangeException">The <paramref name="daysFromNow"/> has to be greater than or equal to <c>0</c>.</exception>
        /// <exception cref="ArgumentNullException">The specified <paramref name="mail"/> is <c>null</c>.</exception>
        public void AddMail(int daysFromNow, Mail mail)
        {
            if (daysFromNow < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(daysFromNow), "The day offset cannot be a negative number!");
            }

            if (mail == null)
            {
                throw new ArgumentNullException(nameof(mail));
            }

            var arrivalDate = SDate.Now().AddDays(daysFromNow);
            var arrivalGameDay = arrivalDate.DaysSinceStart;

            if (HasMailForDayCore(arrivalGameDay, mail.Id))
            {
                string message = $"A mail with the ID \"{mail.Id}\" already exists for the date {arrivalDate}!";

                monitor.Log(message + " Please use a different mail ID!");
                throw new ArgumentException(message);
            }

            // Add the mail to the mail manager. Surface exceptions, if any, as they will indicate
            // errors with the user supplied arguments.
            mailManager.Add(this.modId, mail.Id, arrivalDate);
           

            if (!timedMails.ContainsKey(arrivalGameDay))
            {
                timedMails[arrivalGameDay] = new Dictionary<string, Mail>();
            }

            timedMails[arrivalGameDay].Add(mail.Id, mail);
        }

        /// <summary>
        /// Add a mail to the player's mailbox.
        /// </summary>
        /// <param name="arrivalDay">The day when the mail will arrive in the mailbox.</param>
        /// <param name="mail">The mail to add.</param>
        /// <exception cref="ArgumentOutOfRangeException">The <paramref name="arrivalDay"/> is in the past.</exception>
        /// <exception cref="ArgumentNullException">The specified <paramref name="mail"/> is be <c>null</c>.</exception>
        public void AddMail(SDate arrivalDay, Mail mail)
        {
            if (arrivalDay == null)
            {
                throw new ArgumentNullException(nameof(arrivalDay));
            }

            AddMail(SDateHelper.GetCurrentDayOffsetFromDate(arrivalDay), mail);
        }

        /// <summary>
        /// Determine if a mail added by this mail service already exists for a day.
        /// </summary>
        /// <param name="day">The day to check for.</param>
        /// <param name="mailId">The ID of the mail.</param>
        /// <returns>
        /// <c>true</c> if a mail with the specified <paramref name="mailId"/> has already been added for the specified <paramref name="day"/>;
        /// otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">The specified <paramref name="day"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// The specified <paramref name="mailId"/> is <c>null</c> or does not contain at least one 
        /// non-whitespace character.
        /// </exception>
        public bool HasMailForDay(SDate day, string mailId)
        {
            if (day == null)
            {
                throw new ArgumentNullException(nameof(day));
            }

            if (string.IsNullOrWhiteSpace(mailId))
            {
                throw new ArgumentException("The mail ID needs to contain at least one non-whitespace character!", nameof(mailId));
            }

            var gameDay = day.DaysSinceStart;
            return HasMailForDayCore(gameDay, mailId);
        }

        /// <summary>
        /// Get whether the specified mail was already sent to the player.
        /// </summary>
        /// <param name="mailId">The ID of the mail.</param>
        /// <returns>
        /// <c>true</c> if a mail with the specified <paramref name="mailId"/> was already sent to the player; 
        /// otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The specified <paramref name="mailId"/> is <c>null</c> or does not contain at least one 
        /// non-whitespace character.
        /// </exception>
        public bool HasReceivedMail(string mailId)
        {
            if (string.IsNullOrWhiteSpace(mailId))
            {
                throw new ArgumentException("The mail ID needs to contain at least one non-whitespace character!", nameof(mailId));
            }

            return mailManager.HasReceivedMail(this.modId, mailId);
        }

        /// <summary>
        /// Determine if a mail with the given <paramref name="mailId"/> added by this mail service is currently in the mailbox.
        /// </summary>
        /// <param name="mailId">The ID of the mail.</param>
        /// <returns><c>true</c> if a mail with the specified <paramref name="mailId"/> has already been registered and 
        /// is currently in the mailbox; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentException">
        /// The specified <paramref name="mailId"/> is <c>null</c> or does not contain at least one 
        /// non-whitespace character.
        /// </exception>
        public bool HasRegisteredMailInMailbox(string mailId)
        {
            if (string.IsNullOrWhiteSpace(mailId))
            {
                throw new ArgumentException("The mail ID needs to contain at least one non-whitespace character!", nameof(mailId));
            }

            return mailManager.HasMailInMailbox(this.modId, mailId);
        }

        /// <summary>
        /// Determine if a mail has already been added by this mail service for a specific day.
        /// </summary>
        /// <param name="gameDay">The day to check for.</param>
        /// <param name="mailId">The ID of the mail.</param>
        /// <returns>
        /// <c>true</c> if a mail with the specified <paramref name="mailId"/> has already been added for the specified <paramref name="gameDay"/>; 
        /// otherwise, <c>false</c>.
        /// </returns>
        private bool HasMailForDayCore(int gameDay, string mailId)
        {
            return this.timedMails.ContainsKey(gameDay) && this.timedMails[gameDay].ContainsKey(mailId);
        }

        /// <summary>
        /// Notify an observer that a mail is being opened.
        /// </summary>
        /// <param name="e">Information about the mail being opened.</param>
        void IMailObserver.OnMailOpening(MailOpeningEventArgs e)
        {
            // Raise the mail-opening event.
            this.MailOpening?.Invoke(this, e);
        }

        /// <summary>
        /// Notify an observer that a mail has been closed.
        /// </summary>
        /// <param name="e">Information about the closed mail.</param>
        void IMailObserver.OnMailClosed(MailClosedCoreEventArgs e)
        {
            // Remove the mail from the service. 
            // We don't need to do key checks here because the service is only notified 
            // for closed mails belonging to it.
            this.timedMails[e.ArrivalDay.DaysSinceStart].Remove(e.MailId);

            // Raise the mail-closed event.
            this.MailClosed?.Invoke(this, new MailClosedEventArgs(e.MailId, e.InteractionRecord));
        }

        /// <summary>
        /// Retrieve a mail by its ID and arrival day.
        /// </summary>
        /// <param name="mailId">The ID of the mail. Needs to contain at least one non-whitespace character.</param>
        /// <param name="arrivalDay">The mail's arrival day in the mailbox of the receiver.</param>
        /// <returns>
        /// A <see cref="Mail"/> instance with the specified <paramref name="mailId"/> and <paramref name="arrivalDay"/> on success,
        /// othewise <c>null</c>.
        /// </returns>
        /// <exception cref="ArgumentException">The specified <paramref name="mailId"/> is an invalid mod ID.</exception>
        /// <exception cref="ArgumentNullException">The specified <paramref name="arrivalDay"/> is <c>null</c>.</exception>
        Mail IMailSender.GetMailFromId(string mailId, SDate arrivalDay)
        {
            if (string.IsNullOrWhiteSpace(mailId))
            {
                throw new ArgumentException("The mail ID needs to contain at least one non-whitespace character!", nameof(mailId));
            }

            if (arrivalDay == null)
            {
                throw new ArgumentNullException(nameof(arrivalDay));
            }

            int arrivalGameDay = arrivalDay.DaysSinceStart;
            return !timedMails.TryGetValue(arrivalGameDay, out Dictionary<string, Mail> mailForDay)
                || !mailForDay.TryGetValue(mailId, out Mail mail)
                ? null
                : mail;
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            var saveData = saveDataBuilder.Construct(this.timedMails);
            saveDataHelper.WriteData(this.saveDataKey, saveData);
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            var saveData = saveDataHelper.ReadData<List<MailSaveData>>(this.saveDataKey);

            timedMails = saveData != null
                ? saveDataBuilder.Reconstruct(saveData)
                : new Dictionary<int, Dictionary<string, Mail>>();
        }

        private class MailSaveData
        {
            public MailSaveData() { }

            public MailSaveData(string id, string text, int arrivalDay)
            {
                Id = id;
                AbsoluteArrivalDay = arrivalDay;
                Text = text;
            }

            public MailType MailType { get; set; }

            public string Id { get; set; }

            public string Text { get; set; }

            public int AbsoluteArrivalDay { get; set; }

            #region Item Mail Content

            public List<ItemSaveData> AttachedItemsSaveData { get; set; }

            #endregion // Item Mail Content

            #region Money Mail Content

            public int Money { get; set; }

            #endregion // Money Mail Content

            #region Quest Mail Content

            public int QuestId { get; set; }

            public bool IsAutomaticallyAccepted { get; set; }

            #endregion

            #region Recipe Mail Content

            public string RecipeName { get; set; }

            public RecipeType RecipeType { get; set; }

            #endregion // Recipe Mail Content
        }

        private class SaveDataBuilder
        {
            private readonly ItemSerializer itemSerializer;

            public SaveDataBuilder()
            {
                itemSerializer = new ItemSerializer();
            }

            public List<MailSaveData> Construct(Dictionary<int, Dictionary<string, Mail>> mailList)
            {
                var mailSaveDataList = new List<MailSaveData>();

                foreach (KeyValuePair<int, Dictionary<string, Mail>> daysAndMail in mailList)
                {
                    foreach (var mail in daysAndMail.Value.Values)
                    {
                        var mailSaveData = new MailSaveData(mail.Id, mail.Text, daysAndMail.Key);

                        switch (mail)
                        {
                            case ItemMail itemMail:
                                mailSaveData.MailType = MailType.ItemMail;
                                mailSaveData.AttachedItemsSaveData = itemMail.AttachedItems?.Select(item => itemSerializer.Deconstruct(item)).ToList();
                                break;
                            case MoneyMail moneyMail:
                                mailSaveData.MailType = MailType.MoneyMail;
                                mailSaveData.Money = moneyMail.AttachedMoney;
                                break;
                            case RecipeMail recipeMail:
                                mailSaveData.MailType = MailType.RecipeMail;
                                mailSaveData.RecipeName = recipeMail.RecipeName;
                                mailSaveData.RecipeType = recipeMail.RecipeType;
                                break;
                            case QuestMail questMail:
                                mailSaveData.MailType = MailType.QuestMail;
                                mailSaveData.QuestId = questMail.QuestId;
                                mailSaveData.IsAutomaticallyAccepted = questMail.IsAutomaticallyAccepted;
                                break;
                            default:
                                mailSaveData.MailType = MailType.PlainMail;
                                break;
                        }

                        mailSaveDataList.Add(mailSaveData);
                    }
                }

                return mailSaveDataList;
            }

            public Dictionary<int, Dictionary<string, Mail>> Reconstruct(List<MailSaveData> mailSaveDataList)
            {
                var mailList = new Dictionary<int, Dictionary<string, Mail>>();

                foreach (var mailSaveData in mailSaveDataList)
                {
                    if (!mailList.ContainsKey(mailSaveData.AbsoluteArrivalDay))
                    {
                        mailList[mailSaveData.AbsoluteArrivalDay] = new Dictionary<string, Mail>();
                    }

                    Mail mail = null;
                    switch (mailSaveData.MailType)
                    {
                        case MailType.ItemMail:
                            var attachedItems = mailSaveData.AttachedItemsSaveData.Select(itemSaveData => itemSerializer.Construct(itemSaveData)).ToList();
                            mail = new ItemMail(mailSaveData.Id, mailSaveData.Text, attachedItems);
                            break;
                        case MailType.MoneyMail:
                            mail = new MoneyMail(mailSaveData.Id, mailSaveData.Text, mailSaveData.Money);
                            break;
                        case MailType.RecipeMail:
                            mail = new RecipeMail(mailSaveData.Id, mailSaveData.Text, mailSaveData.RecipeName, mailSaveData.RecipeType);
                            break;
                        case MailType.QuestMail:
                            mail = new QuestMail(mailSaveData.Id, mailSaveData.Text, mailSaveData.QuestId, mailSaveData.IsAutomaticallyAccepted);
                            break;
                        default:
                            mail = new Mail(mailSaveData.Id, mailSaveData.Text);
                            break;

                    }

                    mailList[mailSaveData.AbsoluteArrivalDay].Add(mailSaveData.Id, mail);
                }

                return mailList;
            }
        }
    }
}
