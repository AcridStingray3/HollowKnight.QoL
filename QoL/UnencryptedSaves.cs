﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using Modding;
using Modding.Patches;
using MonoMod.Cil;
using Newtonsoft.Json;
using UnityEngine;

namespace QoL
{
    [UsedImplicitly]
    public class UnencryptedSaves : FauxMod
    {
        private static readonly MethodInfo GET_SAVE_FILE_NAME = typeof(ModHooks).GetMethod("GetSaveFileName", BindingFlags.Instance | BindingFlags.NonPublic);

        public override void Initialize()
        {
            ModHooks.Instance.SavegameLoadHook += OnSaveLoad;
            ModHooks.Instance.BeforeSavegameSaveHook += OnSaveSave;
            IL.DesktopPlatform.WriteSaveSlot += RemoveStupidSave;
        }

        private void RemoveStupidSave(ILContext il)
        {
            ILCursor c = new ILCursor(il).Goto(0);

            while (c.TryFindNext
            (
                out ILCursor[] cursors,
                x => x.MatchLdstr("_1.4.3.2.dat"),
                x => x.MatchCall(typeof(string), nameof(string.Concat)),
                x => x.MatchStloc(5),
                x => x.MatchLdloc(5),
                x => x.MatchLdarg(2),
                x => x.MatchCall(typeof(File), nameof(File.WriteAllBytes)),
                x => x.MatchLeaveS(out _)
            ))
            {
                Log("Matched save.");

                for (int i = cursors.Length - 1; i >= 0; i--)
                {
                    cursors[i].Remove();
                }
            }
        }

        private static void OnSaveSave(SaveGameData data)
        {
            int id = GetRealID(data.playerData.profileID);

            string path = GetSavePath(id, "json");

            string text = JsonConvert.SerializeObject
            (
                data,
                Formatting.Indented,
                new JsonSerializerSettings
                {
                    ContractResolver = ShouldSerializeContractResolver.Instance,
                    TypeNameHandling = TypeNameHandling.Auto
                }
            );

            File.WriteAllText(path, text);

            File.SetLastWriteTime(path, new DateTime(1999, 6, 11));
        }

        private static void OnSaveLoad(int saveSlot)
        {
            saveSlot = GetRealID(saveSlot);

            var gm = GameManager.instance;

            void DoLoad(string text)
            {
                try
                {
                    var saveGameData = JsonConvert.DeserializeObject<SaveGameData>
                    (
                        text,
                        new JsonSerializerSettings
                        {
                            ContractResolver = ShouldSerializeContractResolver.Instance,
                            TypeNameHandling = TypeNameHandling.Auto
                        }
                    );

                    gm.playerData = PlayerData.instance = saveGameData.playerData;
                    gm.sceneData = SceneData.instance = saveGameData.sceneData;
                    gm.profileID = saveSlot;
                    gm.inputHandler.RefreshPlayerData();
                }
                catch (ArgumentException)
                {
                    // It's fine to just stop here as this is *after* the game loads the dat anyways
                }
            }

            string jsonPath = GetSavePath(saveSlot, "json");

            if (!File.Exists(jsonPath)) return;

            DateTime jsonWrite = File.GetLastWriteTimeUtc(jsonPath);

            if (jsonWrite.Year != 1999)
                LoadJson(jsonPath, DoLoad);
        }

        private static void LoadJson(string jsonPath, Action<string> callback)
        {
            string res = null;

            try
            {
                res = Encoding.UTF8.GetString(File.ReadAllBytes(jsonPath));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            CoreLoop.InvokeNext(() => { callback?.Invoke(res); });
        }

        private static string GetSavePath(int saveSlot, string ending)
        {
            return Path.Combine(ReflectionHelper.GetAttr<DesktopPlatform, string>(Platform.Current as DesktopPlatform, "saveDirPath"), $"user{saveSlot}.{ending}");
        }

        private static int GetRealID(int id)
        {
            string s = (string) GET_SAVE_FILE_NAME.Invoke(ModHooks.Instance, new object[] {id});

            return s == null
                ? id
                : int.Parse(new string(s.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()));
        }
    }
}