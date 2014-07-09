using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class DarkLog
    {
        public static Queue<string> messageQueue = new Queue<string>();

        public static void Debug(string message)
        {
            //Use messageQueue if looking for messages that don't normally show up in the log.

            //messageQueue.Enqueue("[" + UnityEngine.Time.realtimeSinceStartup + "] DarkMultiPlayer: " + message);
            UnityEngine.Debug.Log("[" + UnityEngine.Time.realtimeSinceStartup + "] DarkMultiPlayer: " + message);
        }

        public static void Update()
        {
            while (messageQueue.Count > 0)
            {
                string message = messageQueue.Dequeue();
                UnityEngine.Debug.Log(message);
                /*
                using (StreamWriter sw = new StreamWriter("DarkLog.txt", true, System.Text.Encoding.UTF8)) {
                    sw.WriteLine(message);
                }
                */
            }
        }

        public static string PrettyPrintConfigNode(ConfigNode node, string indent = "")
        {
            if (node == null)
            {
                return "";
            }

            string ret = indent + "NodeName: '" + node.name + "'\n";
            ret += indent + "{\n";
            
            int numValues = node.CountValues;
            for (int i = 0; i < numValues; i++)
            {
                var value = node.values[i];
                ret += indent + "Value: '" + value.name + "' = '" + value.value + "'\n";
            }

            int numNodes = node.CountNodes;
            for (int i = 0; i < numNodes; i++)
            {
                var childNode = node.nodes[i];
                ret += PrettyPrintConfigNode(childNode, indent + "\t");
            }
            ret += indent + "}\n";
            return ret;
        }

    }
}
