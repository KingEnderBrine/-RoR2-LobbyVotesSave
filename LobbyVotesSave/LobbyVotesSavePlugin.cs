using BepInEx;
using BepInEx.Logging;
using R2API.Utils;
using RoR2;
using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using UnityEngine;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
namespace LobbyVotesSave
{
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("com.KingEnderBrine.LobbyVotesSave", "Lobby Votes Save", "1.0.0")]
    public class LobbyVotesSavePlugin : BaseUnityPlugin
    {
        internal static LobbyVotesSavePlugin Instance { get; private set; }
        internal static ManualLogSource InstanceLogger { get => Instance?.Logger; }

        private static string SavesDirectory { get; } = System.IO.Path.Combine(Application.persistentDataPath, "LobbyVotesSave");

        private void Awake()
        {
            Instance = this;

            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnLocalUserSignIn += RestoreVotes;
            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnVotesUpdated += StoreVotes;
        }

        private void Destroy()
        {
            Instance = null;

            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnLocalUserSignIn -= RestoreVotes;
            On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.OnVotesUpdated -= StoreVotes;
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

        //RoR.LocalUserBallotPersistenceManager.OnLocalUserSignIn
        private static void RestoreVotes(On.RoR2.PreGameRuleVoteController.LocalUserBallotPersistenceManager.orig_OnLocalUserSignIn orig, RoR2.LocalUser localUser)
        {
            orig(localUser);

            try
            {
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