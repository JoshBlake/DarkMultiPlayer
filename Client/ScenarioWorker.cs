using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ScenarioWorker
    {
        class ScenarioEntry
        {
            public string scenarioName;
            public string scenarioData;
        }

        public bool workerEnabled = false;
        private static ScenarioWorker singleton;
        private Queue<ScenarioEntry> scenarioReceiveQueue = new Queue<ScenarioEntry>();
        private bool blockScenarioDataSends = false;
        private bool loadedScience = false;
        private float lastScenarioSendTime = 0f;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;

        private const string RESEARCH_AND_DEVELOPMENT_NAME = "ResearchAndDevelopment";
        private const string DISCOVERED_OBJECTS_NAME = "ScenarioDiscoverableObjects";

        private ConfigNodeSerializer nodeSerializer = ConfigNodeSerializer.Instance;

        Dictionary<string, string> scenarioModuleCache = new Dictionary<string, string>();
        HashSet<string> sendBlockedScenarioModules = new HashSet<string>();

        public ScenarioWorker()
        {
            //Don't send asteroid data.
            sendBlockedScenarioModules.Add(DISCOVERED_OBJECTS_NAME);
        }

        public static ScenarioWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.updateEvent.Remove(singleton.Update);
                }
                singleton = new ScenarioWorker();
                Client.updateEvent.Add(singleton.Update);
            }
        }

        public void QueueScenarioData(string scenarioName, string scenarioData)
        {
            ScenarioEntry entry = new ScenarioEntry();
            entry.scenarioName = scenarioName;
            entry.scenarioData = scenarioData;
            scenarioReceiveQueue.Enqueue(entry);
        }

        public void LoadReceivedScenarioModuleQueueIntoGame()
        {
            if (scenarioReceiveQueue.Count == 0)
            {
                return;
            }

            while (scenarioReceiveQueue.Count > 0)
            {
                ScenarioEntry entry = scenarioReceiveQueue.Dequeue();
                bool success = LoadScenarioData(entry);

                if (success)
                {
                    CacheScenarioModule(entry.scenarioName, entry.scenarioData);
                    sendBlockedScenarioModules.Remove(entry.scenarioData);
                }
                else
                {
                    sendBlockedScenarioModules.Add(entry.scenarioData);
                }

                if (success && entry.scenarioName == RESEARCH_AND_DEVELOPMENT_NAME)
                {
                    loadedScience = true;
                }
            }
        }

        public void CreateBlankScienceIfScienceMissing()
        {
            if (!IsScenarioModuleCached(RESEARCH_AND_DEVELOPMENT_NAME) && Client.fetch.gameMode == GameMode.CAREER)
            {
                DarkLog.Debug("Creating blank science data");
                ConfigNode newNode = GetBlankResearchAndDevelopmentNode();
                CreateNewProtoScenarioModule(RESEARCH_AND_DEVELOPMENT_NAME, newNode);
            }
        }

        private void Update()
        {
            if (workerEnabled)
            {
                //Load first so we clobber pending changes here rather than clobber the rest of the server
                LoadReceivedScenarioModuleQueueIntoGame();
                SendScenarioModules();
            }
        }

        private void SendScenarioModules()
        {
            List<string> scenarioName = new List<string>();
            List<string> scenarioData = new List<string>();

            string moduleNames = "";

            BlockScienceInSandbox();

            List<ProtoScenarioModule> protoModules = ScenarioRunner.GetUpdatedProtoModules();
            foreach (ProtoScenarioModule psm in protoModules)
            {
                if (psm != null && psm.moduleName != null && psm.moduleRef != null)
                {
                    if (!sendBlockedScenarioModules.Contains(psm.moduleName))
                    {
                        ConfigNode scenarioNode = new ConfigNode();
                        psm.moduleRef.Save(scenarioNode);

                        byte[] data = nodeSerializer.Serialize(scenarioNode);
                        string scenarioNodeString = System.Text.Encoding.UTF8.GetString(data);

                        if (DidScenarioModuleChange(psm.moduleName, scenarioNodeString))
                        {
                            CacheScenarioModule(psm.moduleName, scenarioNodeString);

                            if (moduleNames.Length > 0)
                            {
                                moduleNames += ", ";
                            }
                            moduleNames += psm.moduleName;

                            scenarioName.Add(psm.moduleName);
                            scenarioData.Add(scenarioNodeString);
                        }
                    }
                }
            }
            if (scenarioName.Count > 0)
            {
                DarkLog.Debug("Sending " + scenarioName.Count + " scenario modules: " + moduleNames);
                NetworkWorker.fetch.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
            }
        }

        private bool LoadScenarioData(ScenarioEntry entry)
        {
            if (entry.scenarioName == DISCOVERED_OBJECTS_NAME)
            {
                DarkLog.Debug("Skipping loading asteroid data - It is created locally");
                return false;
            }
            if (entry.scenarioName == RESEARCH_AND_DEVELOPMENT_NAME && Client.fetch.gameMode != GameMode.CAREER)
            {
                DarkLog.Debug("Skipping loading career mode data in sandbox");
                return false;
            }

            DarkLog.Debug("Received scenario module " + entry.scenarioName);

            ConfigNode scenarioNode = nodeSerializer.Deserialize(entry.scenarioData);

            if (scenarioNode == null)
            {
                DarkLog.Debug(entry.scenarioName + " scenario data failed to create a ConfigNode!");
                return false;
            }

            List<ProtoScenarioModule> protoModules = ScenarioRunner.GetUpdatedProtoModules();
            foreach (ProtoScenarioModule psm in protoModules)
            {
                if (psm.moduleName == entry.scenarioName)
                {
                    //This will happen when a client receives an update from another client for a scenario node after connecting
                    return UpdateExistingProtoScenarioModule(entry, scenarioNode, psm);
                }
            }

            //No existing proto scenario module found
            //This will happen on first connection for all clients
            DarkLog.Debug("Creating new " + entry.scenarioName + " scenario module");
            return CreateNewProtoScenarioModule(entry.scenarioName, scenarioNode);
        }

        private bool UpdateExistingProtoScenarioModule(ScenarioEntry entry, ConfigNode scenarioNode, ProtoScenarioModule psm)
        {
            DarkLog.Debug("Updating existing " + entry.scenarioName + " scenario module");
            try
            {
                if (psm.moduleRef != null)
                {
                    ScenarioRunner.RemoveModule(psm.moduleRef);
                }

                psm.moduleRef = ScenarioRunner.fetch.AddModule(scenarioNode);

                //update targetScenes since ScenarioRunner doesn't set them for whatever reason
                psm.moduleRef.targetScenes = psm.targetScenes;

                HighLogic.CurrentGame.scenarios = ScenarioRunner.GetUpdatedProtoModules();
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error loading " + entry.scenarioName + " scenario module, Exception: " + e);
                return false;
            }
            return true;
        }

        private bool CreateNewProtoScenarioModule(string scenarioName, ConfigNode newNode)
        {
            try
            {
                ProtoScenarioModule newModule = new ProtoScenarioModule(newNode);
                HighLogic.CurrentGame.scenarios.Add(newModule);
                newModule.Load(ScenarioRunner.fetch);
                return true;
            }
            catch (Exception ex)
            {
                DarkLog.Debug("Error creating new ProtoScenarioModule: " + scenarioName + " Exception: " + ex.ToString());
                return false;
            }
        }

        private void BlockScienceInSandbox()
        {
            //Don't send research and develpoment to sandbox servers.
            if (Client.fetch.gameMode == GameMode.SANDBOX)
            {
                sendBlockedScenarioModules.Add(RESEARCH_AND_DEVELOPMENT_NAME);
            }
            //we don't remove this block here in case we have to block science elsewhere
        }

        private bool IsScenarioModuleCached(string scenarioName)
        {
            return scenarioModuleCache.ContainsKey(scenarioName);
        }

        private void CacheScenarioModule(string scenarioName, string scenarioNodeString)
        {
            scenarioModuleCache[scenarioName] = scenarioNodeString;
        }

        private bool DidScenarioModuleChange(string moduleName, string scenarioString)
        {
            string previousScenarioString = null;
            if (scenarioModuleCache.TryGetValue(moduleName, out previousScenarioString))
            {
                return previousScenarioString != scenarioString;
            }
            //not in cache => yes the module changed. Make us try to send if we never sent this module before.
            return true;
        }

        //Would be nice if we could ask KSP to do this for us...
        private ConfigNode GetBlankResearchAndDevelopmentNode()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "ResearchAndDevelopment");
            newNode.AddValue("scene", "5, 6, 7, 8, 9");
            newNode.AddValue("sci", "0");
            newNode.AddNode("Tech");
            newNode.GetNode("Tech").AddValue("id", "start");
            newNode.GetNode("Tech").AddValue("state", "Available");
            newNode.GetNode("Tech").AddValue("part", "mk1pod");
            newNode.GetNode("Tech").AddValue("part", "liquidEngine");
            newNode.GetNode("Tech").AddValue("part", "solidBooster");
            newNode.GetNode("Tech").AddValue("part", "fuelTankSmall");
            newNode.GetNode("Tech").AddValue("part", "trussPiece1x");
            newNode.GetNode("Tech").AddValue("part", "longAntenna");
            newNode.GetNode("Tech").AddValue("part", "parachuteSingle");
            return newNode;
        }
    }
}

