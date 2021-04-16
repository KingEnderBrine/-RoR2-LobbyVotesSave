using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.UI;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Permissions;
using UnityEngine;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
namespace LobbyVotesSave
{
    [BepInDependency("com.KingEnderBrine.InLobbyConfig", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.KingEnderBrine.LobbyVotesSave", "Lobby Votes Save", "1.2.1")]
    public class LobbyVotesSavePlugin : BaseUnityPlugin
    {
        internal static LobbyVotesSavePlugin Instance { get; private set; }
        internal static ManualLogSource InstanceLogger { get => Instance?.Logger; }

        private static string SavesDirectory { get; } = System.IO.Path.Combine(Application.persistentDataPath, "LobbyVotesSave");

        private static CharacterSelectController cachedCharacterSelectController;
        private static ConfigEntry<bool> Enabled { get; set; }

        private void Awake()
        {
            Instance = this;

            SetupConfig();

            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnLocalUserSignIn += RestoreVotes;
            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnVotesUpdated += StoreVotes;

            On.RoR2.NetworkUser.SetBodyPreference += StoreBodyPreference;
            IL.RoR2.NetworkUser.Start += NetworkUserStartIL;

            On.RoR2.UI.CharacterSelectController.Start += CacheCharacterSelectController;
        }

        private void Destroy()
        {
            Instance = null;

            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnLocalUserSignIn -= RestoreVotes;
            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnVotesUpdated -= StoreVotes;

            On.RoR2.NetworkUser.SetBodyPreference -= StoreBodyPreference;
            IL.RoR2.NetworkUser.Start -= NetworkUserStartIL;

            On.RoR2.UI.CharacterSelectController.Start -= CacheCharacterSelectController;
        }

        private void SetupConfig()
        {
            Enabled = Config.Bind("Main", "Enabled", true, "Load stored values when enter lobby");
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.KingEnderBrine.InLobbyConfig"))
            {
                SetupInLobbyConfig();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private void SetupInLobbyConfig()
        {
            InLobbyConfig.ModConfigCatalog.Add(InLobbyConfig.Fields.ConfigFieldUtilities.CreateFromBepInExConfigFile(Config, "Lobby votes save"));
        }

        private void CacheCharacterSelectController(On.RoR2.UI.CharacterSelectController.orig_Start orig, RoR2.UI.CharacterSelectController self)
        {
            orig(self);
            cachedCharacterSelectController = self;
        }

        private static void StoreVotes(On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.orig_OnVotesUpdated orig)
        {
            orig();

            try
            {
                Directory.CreateDirectory(SavesDirectory);
                foreach (var row in PreGameRuleVoteController.LocalUserBallotPersistenceManager.votesCache)
                {
                    if (row.Value == null)
                    {
                        continue;
                    }
                    var path = System.IO.Path.Combine(SavesDirectory, row.Key.userProfile.fileName);
                    File.WriteAllBytes(path, row.Value.Select(el => el.internalValue).ToArray());
                }
            }
            catch (Exception e)
            {
                InstanceLogger.LogWarning("Failed to save votes");
                InstanceLogger.LogError(e);
            }
        }

        private static void RestoreVotes(On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.orig_OnLocalUserSignIn orig, RoR2.LocalUser localUser)
        {
            orig(localUser);

            try
            {
                if (!Enabled.Value)
                {
                    return;
                }
                var path = System.IO.Path.Combine(SavesDirectory, localUser.userProfile.fileName);
                if (File.Exists(path))
                {
                    PreGameRuleVoteController.LocalUserBallotPersistenceManager.votesCache[localUser] = File.ReadAllBytes(path).Select(el => new PreGameRuleVoteController.Vote { internalValue = el }).ToArray();
                }
            }
            catch (Exception e)
            {
                InstanceLogger.LogWarning("Failed to load votes");
                InstanceLogger.LogError(e);
            }
        }

        private void NetworkUserStartIL(MonoMod.Cil.ILContext il)
        {
            var c = new ILCursor(il);

            c.GotoNext(
                x => x.MatchCallOrCallvirt(typeof(RoR2.NetworkUser).GetMethod("CallCmdSetBodyPreference", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)));
            var callLabel = c.Next;

            c.GotoPrev(x => x.MatchLdstr("CommandoBody"));

            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<NetworkUser, BodyIndex>>(RestoreBodyPreference);
            c.Emit(OpCodes.Dup);
            c.Emit(OpCodes.Ldc_I4_1);
            c.Emit(OpCodes.Add);
            c.Emit(OpCodes.Brtrue, callLabel);
            c.Emit(OpCodes.Pop);
        }

        private static BodyIndex RestoreBodyPreference(NetworkUser networkUser)
        {
            try
            {
                if (!Enabled.Value)
                {
                    return BodyIndex.None;
                }

                if (PreGameController.instance && PreGameController.instance.gameModeIndex == GameModeCatalog.FindGameModeIndex("EclipseRun"))
                {
                    return BodyIndex.None;
                }
                var path = System.IO.Path.Combine(SavesDirectory, $"{networkUser.localUser.userProfile.fileName}_body");
                if (File.Exists(path))
                {
                    var survivorIndex = (SurvivorIndex)int.Parse(File.ReadAllText(path));
                    var survivorDef = SurvivorCatalog.GetSurvivorDef(survivorIndex);
                    if (survivorDef == null)
                    {
                        return BodyIndex.None;
                    }
                    
                    if (survivorDef.hidden)
                    {
                        return BodyIndex.None;
                    }

                    if (survivorDef.unlockableDef != null)
                    {
                        if (!networkUser.localUser.userProfile.statSheet.HasUnlockable(survivorDef.unlockableDef))
                        {
                            return BodyIndex.None;
                        }
                    }

                    cachedCharacterSelectController?.SelectSurvivor(survivorIndex);

                    return SurvivorCatalog.GetBodyIndexFromSurvivorIndex(survivorIndex);
                }
            }
            catch (Exception e)
            {
                InstanceLogger.LogWarning("Failed to load body preference");
                InstanceLogger.LogError(e);
            }
            return BodyIndex.None;
        }

        private static void StoreBodyPreference(On.RoR2.NetworkUser.orig_SetBodyPreference orig, NetworkUser self, BodyIndex newBodyIndexPreference)
        {
            orig(self, newBodyIndexPreference);

            try
            {
                if (!self.isLocalPlayer || self?.localUser?.userProfile == null)
                {
                    return;
                }
                Directory.CreateDirectory(SavesDirectory);
                var path = System.IO.Path.Combine(SavesDirectory, $"{self.localUser.userProfile.fileName}_body");
                File.WriteAllText(path, ((int)SurvivorCatalog.GetSurvivorIndexFromBodyIndex(newBodyIndexPreference)).ToString());
            }
            catch (Exception e)
            {
                InstanceLogger.LogWarning("Failed to save body preference");
                InstanceLogger.LogError(e);
            }
        }
    }
}