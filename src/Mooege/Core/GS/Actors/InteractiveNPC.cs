﻿/*
 * Copyright (C) 2011 - 2012 mooege project - http://www.mooege.org
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */

using System;
using System.Linq;
using System.Collections.Generic;
using Mooege.Core.GS.Map;
using Mooege.Core.GS.Players;
using Mooege.Net.GS.Message;
using Mooege.Net.GS.Message.Definitions.World;
using Mooege.Core.GS.Actors.Interactions;
using Mooege.Net.GS.Message.Fields;
using Mooege.Net.GS.Message.Definitions.NPC;
using Mooege.Net.GS;
using Mooege.Net.GS.Message.Definitions.Hireling;
using Mooege.Core.GS.Games;
using Mooege.Core.GS.Common.Types.TagMap;
using Mooege.Net.GS.Message.Definitions.Artisan;
using Mooege.Common.Logging;

namespace Mooege.Core.GS.Actors
{
    public class InteractiveNPC : NPC, IMessageConsumer
    {
        Logger logger = new Logger("InteractiveNpc");
        public List<IInteraction> Interactions { get; private set; }
        public List<ConversationInteraction> Conversations { get; private set; }
    
        public InteractiveNPC(World world, int snoId, TagMap tags)
            : base(world, snoId, tags)
        {
            this.Attributes[GameAttribute.NPC_Has_Interact_Options, 0] = true;
            this.Attributes[GameAttribute.NPC_Is_Operatable] = true;
            //this.Attributes[GameAttribute.Buff_Visual_Effect, 0x00FFFFF] = true;
            Interactions = new List<IInteraction>();
            Conversations = new List<ConversationInteraction>();

            foreach(var quest in World.Game.Quests)
                quest.OnQuestProgress += new Games.Quest.QuestProgressDelegate(quest_OnQuestProgress);
            UpdateConversationList(); // show conversations with no quest dependency
        }

        void quest_OnQuestProgress(Quest quest)
        {
            UpdateConversationList();
        }

        private void UpdateConversationList()
        {
            if (ConversationList != null)
            {
                var ConversationsNew = new List<int>();
                foreach (var entry in ConversationList.ConversationListEntries)
                {
                    if (entry.SNOLevelArea == -1 && entry.SNOQuestActive == -1 && entry.SNOQuestAssigned == -1 && entry.SNOQuestComplete == -1 && entry.SNOQuestCurrent == -1 && entry.SNOQuestRange == -1)
                        ConversationsNew.Add(entry.SNOConv);

                    if (Mooege.Common.MPQ.MPQStorage.Data.Assets[Common.Types.SNO.SNOGroup.QuestRange].ContainsKey(entry.SNOQuestRange))
                        if (World.Game.Quests.IsInQuestRange(Mooege.Common.MPQ.MPQStorage.Data.Assets[Common.Types.SNO.SNOGroup.QuestRange][entry.SNOQuestRange].Data as Mooege.Common.MPQ.FileFormats.QuestRange))
                            ConversationsNew.Add(entry.SNOConv);

                    if (World.Game.Quests.HasCurrentQuest(entry.SNOQuestCurrent, entry.I3))
                        ConversationsNew.Add(entry.SNOConv);
                }

                // remove outdates conversation options and add new ones
                Conversations = Conversations.Where(x => ConversationsNew.Contains(x.ConversationSNO)).ToList();
                foreach (var sno in ConversationsNew)
                    if (!Conversations.Select(x => x.ConversationSNO).Contains(sno))
                        Conversations.Add(new ConversationInteraction(sno));

                // search for an unread questconversation
                bool questConversation = false;
                foreach (var conversation in Conversations)
                    if (Mooege.Common.MPQ.MPQStorage.Data.Assets[Common.Types.SNO.SNOGroup.Conversation].ContainsKey(conversation.ConversationSNO))
                        if ((Mooege.Common.MPQ.MPQStorage.Data.Assets[Common.Types.SNO.SNOGroup.Conversation][conversation.ConversationSNO].Data as Mooege.Common.MPQ.FileFormats.Conversation).I0 == 1)
                            if (conversation.Read == false)
                                questConversation = true;

                // show the exclamation mark if actor has an unread quest conversation and make the actor operable again.
                if (questConversation)
                {
                    Attributes[GameAttribute.Conversation_Icon, 0] = questConversation ? 1 : 0;
                    Attributes[GameAttributeB.NPC_Is_Operatable] = true;
                    Attributes.BroadcastChangedIfRevealed();
                }
            }
        }


        public override void OnTargeted(Player player, TargetMessage message)
        {
            player.SelectedNPC = this;

            var count = Interactions.Count + Conversations.Count;
            if (count == 0)
                return;

            foreach (var _conversation in player.SelectedNPC.Conversations)
            {
                //logger.Debug("Conversation available for actor {0} : {1}.", player.SelectedNPC.NameSNOId, _conversation.ConversationSNO.ToString());
                try
                {
                    var conversation = ((Mooege.Common.MPQ.FileFormats.Conversation)(Mooege.Common.MPQ.MPQStorage.Data.Assets[Common.Types.SNO.SNOGroup.Conversation][_conversation.ConversationSNO].Data));
                    //logger.Debug("Conversation Type: {0}", conversation.ConversationType.ToString());
                    //Unreaded AmbientGossips plays onTargeted, not by Conversation menu selection.
                    if (conversation.ConversationType.ToString().Contains("AmbientGossip") == true && !_conversation.Read)
                    {
                        player.Conversations.StartConversation(_conversation.ConversationSNO);
                        _conversation.MarkAsRead();
                        UpdateConversationList();
                        return;
                    }
                    //If you had already accepted the quest, it shouldnt keep showing over the NPC Conversation Menu.
                    if (conversation.ConversationType.ToString().Contains("QuestEvent") == true && !_conversation.Read)
                    {
                        Attributes[GameAttribute.Conversation_Icon, 0] = 0;
                        Attributes[GameAttributeB.NPC_Is_Operatable] = false;
                        Attributes.BroadcastChangedIfRevealed();   
                        player.Conversations.StartConversation(_conversation.ConversationSNO);
                        _conversation.MarkAsRead();                                                                 
                        return;
                    }
                }
                catch
                {
                    logger.Error("Error while retrieving Conversation info for Actor {0}", this.ActorSNO.Name);
                    return;
                }
            }
            // If there is only one conversation option, immediatly select it without showing menu.
            // Update: This is not how actual beta handles Conversations when only 1 is available, you get to pick the conversation
            // from the menu aswell. Still there are some KIND of conversations that do play on InteractiveNPC first clic, so..
            // TODO: Trigger proper OnTargeted conversations.
            
            /* The code below plays conversations instantly if there is only 1 conversations available in the actor conversationList.
            if (Interactions.Count == 0 && Conversations.Count == 1)
            {
                player.Conversations.StartConversation(Conversations[0].ConversationSNO);
                Conversations[0].MarkAsRead();
                UpdateConversationList();
                return;
            }*/

            NPCInteraction[] npcInters = new NPCInteraction[count];

            var _interaction = 0;
            foreach(var conversation in Conversations)
            {
                npcInters[_interaction] = conversation.AsNPCInteraction(this, player);
                _interaction++;
            }

            foreach(var interaction in Interactions)
            {
                npcInters[_interaction] = interaction.AsNPCInteraction(this, player);
                _interaction++;
            }


            player.InGameClient.SendMessage(new NPCInteractOptionsMessage()
            {
                ActorID = this.DynamicID,
                tNPCInteraction = npcInters,
                Type = NPCInteractOptionsType.Normal             
            });

            // TODO: this has no effect, why is it sent?
            player.InGameClient.SendMessage(new Mooege.Net.GS.Message.Definitions.Effect.PlayEffectMessage()
            {
                ActorId = this.DynamicID,
                Effect = Net.GS.Message.Definitions.Effect.Effect.Unknown36
            }); 
        }

        public void Consume(GameClient client, GameMessage message)
        {
            if (message is NPCSelectConversationMessage) OnSelectConversation(client.Player, message as NPCSelectConversationMessage);
            if (message is HirelingHireMessage) OnHire(client.Player);
            if (message is HirelingInventoryMessage) OnInventory(client.Player);
            if (message is CraftInteractionMessage) OnCraft(client.Player);
            else return;
        }

        public virtual void OnCraft(Player player)
        {
            throw new NotImplementedException();
        }

        public virtual void OnInventory(Player player)
        {
            throw new NotImplementedException();
        }

        public virtual void OnHire(Player player)
        {
            throw new NotImplementedException();
        }

        private void OnSelectConversation(Player player, NPCSelectConversationMessage message)
        {
            var conversation = Conversations.FirstOrDefault(conv => conv.ConversationSNO == message.ConversationSNO);
            if (conversation == null) 
                return;

             player.Conversations.StartConversation(conversation.ConversationSNO);
             conversation.MarkAsRead();
        }
    }
}