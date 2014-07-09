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
        public bool workerEnabled = false;
        private static ScenarioWorker singleton;
        private Queue<ScenarioEntry> scenarioQueue = new Queue<ScenarioEntry>();
        private bool blockScenarioDataSends = false;
        private bool loadedScience = false;
        private float lastScenarioSendTime = 0f;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;

        private const string ResearchDevelopmentScenarioName = "ResearchAndDevelopment";

        private ConfigNodeSerializer nodeSerializer = ConfigNodeSerializer.Instance;

        public static ScenarioWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            if (workerEnabled)
            {
                if (!blockScenarioDataSends)
                {
                    if ((UnityEngine.Time.realtimeSinceStartup - lastScenarioSendTime) > SEND_SCENARIO_DATA_INTERVAL)
                    {
                        lastScenarioSendTime = UnityEngine.Time.realtimeSinceStartup;
                        SendScenarioModules();
                    }
                }
                LoadScenarioDataIntoGame();
            }
        }

        private void SendScenarioModules()
        {
            List<string> scenarioName = new List<string>();
            List<string> scenarioData = new List<string>();
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                //Skip sending science data in sandbox mode (If this can even happen?)
                if (psm != null ? (psm.moduleName != null && psm.moduleRef != null) : false)
                {
                    //Don't send research and develpoment to sandbox servers. Also don't send asteroid data.
                    if (!(psm.moduleName == ResearchDevelopmentScenarioName && Client.fetch.gameMode == GameMode.SANDBOX) && psm.moduleName != "ScenarioDiscoverableObjects")
                    {
                        ConfigNode scenarioNode = new ConfigNode();
                        psm.moduleRef.Save(scenarioNode);

                        string debugString = DarkLog.PrettyPrintConfigNode(scenarioNode);
                        DarkLog.Debug("Sending scenarioNode:\n" + debugString);

                        scenarioName.Add(psm.moduleName);
                        byte[] data = nodeSerializer.Serialize(scenarioNode);
                        string scenarioNodeString = System.Text.Encoding.UTF8.GetString(data);

                        scenarioData.Add(scenarioNodeString);
                    }
                }
            }
            if (scenarioName.Count > 0)
            {
                DarkLog.Debug("Sending " + scenarioName.Count + " scenario modules");
                NetworkWorker.fetch.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
            }
        }

        public void LoadScenarioDataIntoGame()
        {
            if (scenarioQueue.Count == 0)
            {
                return;
            }

            while (scenarioQueue.Count > 0)
            {
                ScenarioEntry entry = scenarioQueue.Dequeue();
                bool success = LoadScenarioData(entry);
                if (success && entry.scenarioName == ResearchDevelopmentScenarioName)
                {
                    loadedScience = true;

                    //TODO hack - really we want to block on a per scenario module basis
                    blockScenarioDataSends = false;
                }
            }
            if (!loadedScience && Client.fetch.gameMode == GameMode.CAREER)
            {
                DarkLog.Debug("Creating blank science data");
                ConfigNode newNode = GetBlankResearchAndDevelopmentNode();
                CreateNewProtoScenarioModule(newNode);
            }
        }

        public bool LoadScenarioData(ScenarioEntry entry)
        {
            if (entry.scenarioName == "ScenarioDiscoverableObjects")
            {
                DarkLog.Debug("Skipping loading asteroid data - It is created locally");
                return false;
            }
            if (entry.scenarioName == ResearchDevelopmentScenarioName && Client.fetch.gameMode != GameMode.CAREER)
            {
                DarkLog.Debug("Skipping loading career mode data in sandbox");
                return false;
            }

            DarkLog.Debug("Received scenario module " + entry.scenarioName);

            ConfigNode scenarioNode = nodeSerializer.Deserialize(entry.scenarioData);

            if (scenarioNode == null)
            {
                DarkLog.Debug(entry.scenarioName + " scenario data failed to create a ConfigNode!");
                blockScenarioDataSends = true;
                return false;
            }
            
            string scenarioNodeDebugString = DarkLog.PrettyPrintConfigNode(scenarioNode);
            DarkLog.Debug("Searching for existing " + entry.scenarioName + " scenario modules... Data:\n" + scenarioNodeDebugString);

            bool protoScenarioModuleFound = false;
            List<ProtoScenarioModule> protoModules = HighLogic.CurrentGame.scenarios;
            foreach (ProtoScenarioModule psm in protoModules)
            {
                if (psm.moduleName == entry.scenarioName)
                {
                    DarkLog.Debug("Updating existing " + entry.scenarioName + " scenario module");
                    protoScenarioModuleFound = true;
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
                        blockScenarioDataSends = true;
                        return false;
                    }
                    break;
                }
            }
            if (!protoScenarioModuleFound)
            {
                DarkLog.Debug("Loading new " + entry.scenarioName + " scenario module");
                return CreateNewProtoScenarioModule(scenarioNode);
            }
            return true;
        }

        private bool CreateNewProtoScenarioModule(ConfigNode newNode)
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
                DarkLog.Debug("Error loading new ProtoScenarioModule: " + newNode.GetValue("name") + " Exception: " + ex.ToString());
                blockScenarioDataSends = true;
                return false;
            }
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

        public void QueueScenarioData(string scenarioName, string scenarioData)
        {
            ScenarioEntry entry = new ScenarioEntry();
            entry.scenarioName = scenarioName;
            entry.scenarioData = scenarioData;
            scenarioQueue.Enqueue(entry);
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
    }

    public class ScenarioEntry
    {
        public string scenarioName;
        public string scenarioData;
    }
}

