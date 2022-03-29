using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using UnityEngine;

[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
[assembly: AssemblyVersion(LobbyVotesSave.LobbyVotesSavePlugin.Version)]
namespace LobbyVotesSave
{
    [BepInDependency(InLobbyConfigIntegration.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(GUID, Name, Version)]
    public class LobbyVotesSavePlugin : BaseUnityPlugin
    {
        public const string GUID = "com.KingEnderBrine.LobbyVotesSave";
        public const string Name = "Lobby Votes Save";
        public const string Version = "1.3.0";

        internal static LobbyVotesSavePlugin Instance { get; private set; }
        internal static ManualLogSource InstanceLogger { get => Instance?.Logger; }

        private static string SavesDirectory { get; } = System.IO.Path.Combine(Application.persistentDataPath, "LobbyVotesSave");

        internal static ConfigEntry<bool> IsEnabled { get; set; }

        private void Start()
        {
            Instance = this;

            SetupConfig();

            InLobbyConfigIntegration.OnStart();

            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnLocalUserSignIn += RestoreVotes;
            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnVotesUpdated += StoreVotes;
        }

        private void Destroy()
        {
            Instance = null;

            InLobbyConfigIntegration.OnDestroy();

            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnLocalUserSignIn -= RestoreVotes;
            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnVotesUpdated -= StoreVotes;
        }

        private void SetupConfig()
        {
            IsEnabled = Config.Bind("Main", "Enabled", true, "Load stored values when enter lobby");
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
                if (!IsEnabled.Value)
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
    }
}