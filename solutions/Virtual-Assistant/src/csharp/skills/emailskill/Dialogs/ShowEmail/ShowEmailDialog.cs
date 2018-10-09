﻿using EmailSkill.Dialogs.Shared.Resources;
using EmailSkill.Dialogs.ShowEmail.Resources;
using EmailSkill.Extensions;
using Luis;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Solutions.Extensions;
using Microsoft.Bot.Solutions.Skills;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EmailSkill
{
    public class ShowEmailDialog : EmailSkillDialog
    {
        public ShowEmailDialog(
            SkillConfiguration services,
            IStatePropertyAccessor<EmailSkillState> emailStateAccessor,
            IStatePropertyAccessor<DialogState> dialogStateAccessor,
            IMailSkillServiceManager serviceManager)
            : base(nameof(ShowEmailDialog), services, emailStateAccessor, dialogStateAccessor, serviceManager)
        {
            var showEmail = new WaterfallStep[]
            {
                IfClearContextStep,
                GetAuthToken,
                AfterGetAuthToken,
                ShowEmailsWithoutEnd,
                PromptToRead,
                CallReadEmailDialog,
            };

            var readEmail = new WaterfallStep[]
            {
                ReadEmail,
                AfterReadOutEmail,
            };

            // Define the conversation flow using a waterfall model.
            AddDialog(new WaterfallDialog(Action.Show, showEmail));
            AddDialog(new WaterfallDialog(Action.Read, readEmail));
            InitialDialogId = Action.Show;
        }

        public async Task<DialogTurnResult> IfClearContextStep(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                // clear context before show emails, and extract it from luis result again.
                var state = await _emailStateAccessor.GetAsync(sc.Context);
                var luisResult = state.LuisResult;

                var topIntent = luisResult?.TopIntent().intent;
                if (topIntent == Email.Intent.CheckMessages)
                {
                    await ClearConversationState(sc);
                    await DigestEmailLuisResult(sc, luisResult);
                }

                if (topIntent == Email.Intent.ShowNext)
                {
                    state.ShowEmailIndex++;
                }

                if (topIntent == Email.Intent.ShowPrevious && state.ShowEmailIndex > 0)
                {
                    state.ShowEmailIndex--;
                }

                return await sc.NextAsync();
            }
            catch (Exception ex)
            {
                throw await HandleDialogExceptions(sc, ex);
            }
        }

        public async Task<DialogTurnResult> ShowEmailsWithoutEnd(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var state = await _emailStateAccessor.GetAsync(sc.Context);

                var messages = await GetMessagesAsync(sc);
                if (messages.Count > 0)
                {
                    await ShowMailList(sc, messages);
                    state.MessageList = messages;
                    return await sc.NextAsync();
                }
                else
                {
                    await sc.Context.SendActivityAsync(sc.Context.Activity.CreateReply(EmailSharedResponses.EmailNotFound, _responseBuilder));
                    return await sc.EndDialogAsync(true);
                }
            }
            catch (Exception ex)
            {
                throw await HandleDialogExceptions(sc, ex);
            }
        }

        public async Task<DialogTurnResult> PromptToRead(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                return await sc.PromptAsync(Action.Prompt, new PromptOptions { Prompt = sc.Context.Activity.CreateReply(ShowEmailResponses.ReadOutPrompt) });
            }
            catch (Exception ex)
            {
                throw await HandleDialogExceptions(sc, ex);
            }
        }

        public async Task<DialogTurnResult> CallReadEmailDialog(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                return await sc.BeginDialogAsync(Action.Read);
            }
            catch (Exception ex)
            {
                throw await HandleDialogExceptions(sc, ex);
            }
        }

        public async Task<DialogTurnResult> ReadEmail(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var state = await _emailStateAccessor.GetAsync(sc.Context);
                var luisResult = state.LuisResult;

                var topIntent = luisResult?.TopIntent().intent;
                if (topIntent == null)
                {
                    return await sc.EndDialogAsync(true);
                }

                var message = state.Message.FirstOrDefault();
                if (topIntent == Email.Intent.ConfirmNo)
                {
                    await sc.Context.SendActivityAsync(
                        sc.Context.Activity.CreateReply(EmailSharedResponses.CancellingMessage));
                    return await sc.EndDialogAsync(true);
                }
                else if (topIntent == Email.Intent.ReadAloud && message == null)
                {
                    return await sc.PromptAsync(Action.Prompt, new PromptOptions { Prompt = sc.Context.Activity.CreateReply(ShowEmailResponses.ReadOutPrompt), });
                }
                else if (topIntent == Email.Intent.SelectItem || (topIntent == Email.Intent.ReadAloud && message != null))
                {
                    var nameListString = $"To: {message?.ToRecipients.FirstOrDefault()?.EmailAddress.Name}";
                    if (message?.ToRecipients.Count() > 1)
                    {
                        nameListString += $" + {message.ToRecipients.Count() - 1} more";
                    }

                    var emailCard = new EmailCardData
                    {
                        Subject = message?.Subject,
                        Sender = message?.Sender.EmailAddress.Name,
                        NameList = nameListString,
                        EmailContent = message?.BodyPreview,
                        EmailLink = message?.WebLink,
                        ReceivedDateTime = message?.ReceivedDateTime == null
                            ? "Not available"
                            : message.ReceivedDateTime.Value.UtcDateTime.ToRelativeString(state.GetUserTimeZone()),
                        Speak = message?.Subject + " From " + message?.Sender.EmailAddress.Name + " " + message?.BodyPreview,
                    };
                    var replyMessage = sc.Context.Activity.CreateAdaptiveCardReply(ShowEmailResponses.ReadOutMessage, "Dialogs/Shared/Resources/Cards/EmailDetailCard.json", emailCard);
                    await sc.Context.SendActivityAsync(replyMessage);

                    return await sc.PromptAsync(Action.Prompt, new PromptOptions { Prompt = sc.Context.Activity.CreateReply(ShowEmailResponses.ReadOutMorePrompt) });
                }
                else
                {
                    return await sc.NextAsync();
                }
            }
            catch (Exception ex)
            {
                throw await HandleDialogExceptions(sc, ex);
            }
        }

        public async Task<DialogTurnResult> AfterReadOutEmail(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var state = await _emailStateAccessor.GetAsync(sc.Context);
                var luisResult = state.LuisResult;

                var topIntent = luisResult?.TopIntent().intent;
                if (topIntent == null)
                {
                    return await sc.EndDialogAsync(true);
                }

                if (topIntent == Email.Intent.ReadAloud || topIntent == Email.Intent.SelectItem)
                {
                    return await sc.BeginDialogAsync(Action.Read);
                }
                else
                {
                    // return a signal for main flow need to start a new ComponentDialog.
                    return await sc.EndDialogAsync(true);
                }
            }
            catch (Exception ex)
            {
                throw await HandleDialogExceptions(sc, ex);
            }
        }
    }
}