﻿using System.Collections.Generic;
using Cottle.Values;
using EddiSpeechService;
using Utilities;
using Newtonsoft.Json;
using EddiEvents;
using Eddi;
using System.Windows.Controls;
using System;
using System.Text.RegularExpressions;
using System.IO;

namespace EddiSpeechResponder
{
    /// <summary>
    /// A responder that responds to events with a speech
    /// </summary>
    public class SpeechResponder : EDDIResponder
    {
        // The file to log speech
        public static readonly string LogFile = Constants.DATA_DIR + @"\speechresponder.out";

        private ScriptResolver scriptResolver;

        private bool subtitles;

        private bool subtitlesOnly;

        public string ResponderName()
        {
            return "Speech responder";
        }

        public string ResponderVersion()
        {
            return "1.0.0";
        }

        public string ResponderDescription()
        {
            return "Respond to events with pre-scripted responses using the information available.  Not all events have scripted responses by default; those that do not have the 'Test' button disabled.  The default personality can be copied, which allows existing scripts to be modified or disabled, and new scripts to be written, to suit your preferences.";
        }

        public SpeechResponder()
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            Personality personality = null;
            if (configuration != null && configuration.Personality != null)
            {
                personality = Personality.FromName(configuration.Personality);
            }
            if (personality == null)
            { 
                personality = Personality.Default();
            }
            scriptResolver = new ScriptResolver(personality.Scripts);
            subtitles = configuration.Subtitles;
            subtitlesOnly = configuration.SubtitlesOnly;
            Logging.Info("Initialised " + ResponderName() + " " + ResponderVersion());
        }

        /// <summary>
        /// Change the personality for the speech responder
        /// </summary>
        /// <returns>true if the speech responder is now using the new personality, otherwise false</returns>
        public bool SetPersonality(string newPersonality)
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            if (newPersonality == configuration.Personality)
            {
                // Already set to this personality
                return true;
            }

            // Ensure that this personality exists
            Personality personality = Personality.FromName(newPersonality);
            if (personality != null)
            {
                // Yes it does; use it
                configuration.Personality = newPersonality;
                configuration.ToFile();
                scriptResolver = new ScriptResolver(personality.Scripts);
                Logging.Debug("Changed personality to " + newPersonality);
                return true;
            }
            else
            {
                // No it does not; ignore it
                return false;
            }
        }

        public bool Start()
        {
            return true;
        }

        public void Stop()
        {
        }

        public void Reload()
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            Personality personality = Personality.FromName(configuration.Personality);
            if (personality == null)
            {
                Logging.Warn("Failed to find named personality; falling back to default");
                personality = Personality.Default();
                configuration.Personality = personality.Name;
                configuration.ToFile();
            }
            scriptResolver = new ScriptResolver(personality.Scripts);
            subtitles = configuration.Subtitles;
            subtitlesOnly = configuration.SubtitlesOnly;
            Logging.Info("Reloaded " + ResponderName() + " " + ResponderVersion());
        }

        public void Handle(Event theEvent)
        {
            Logging.Debug("Received event " + JsonConvert.SerializeObject(theEvent));
            Say(scriptResolver, theEvent.type, theEvent);
        }

        // Say something with the default resolver
        public void Say(string scriptName, Event theEvent = null, int? priority = null, string voice = null, bool? wait = null)
        {
            Say(scriptResolver, scriptName, theEvent, priority, voice);
        }

        // Say something with a custom resolver
        public void Say(ScriptResolver resolver, string scriptName, Event theEvent = null, int? priority = null, string voice = null, bool? wait = null)
        {
            Dictionary<string, Cottle.Value> dict = createVariables(theEvent);
            string speech = resolver.resolve(scriptName, dict);
            if (speech != null)
            {
                if (subtitles)
                {
                    // Log a tidied version of the speech
                    log(Regex.Replace(speech, "<.*?>", string.Empty));
                }
                if (!(subtitles && subtitlesOnly))
                {
                    SpeechService.Instance.Say(EDDI.Instance.Ship, speech, (wait == null ? true : (bool)wait), (priority == null ? resolver.priority(scriptName) : (int)priority), voice);
                }
            }
        }

        // Create Cottle variables from the EDDI information
        private Dictionary<string, Cottle.Value> createVariables(Event theEvent = null)
        {
            Dictionary<string, Cottle.Value> dict = new Dictionary<string, Cottle.Value>();

            dict["vehicle"] = EDDI.Instance.Vehicle;
            dict["environment"] = EDDI.Instance.Environment;

            if (EDDI.Instance.Cmdr != null)
            {
                dict["cmdr"] = new ReflectionValue(EDDI.Instance.Cmdr);
            }

            if (EDDI.Instance.Ship != null)
            {
                dict["ship"] = new ReflectionValue(EDDI.Instance.Ship);
            }

            if (EDDI.Instance.HomeStarSystem != null)
            {
                dict["homesystem"] = new ReflectionValue(EDDI.Instance.HomeStarSystem);
            }

            if (EDDI.Instance.HomeStation != null)
            {
                dict["homestation"] = new ReflectionValue(EDDI.Instance.HomeStation);
            }

            if (EDDI.Instance.CurrentStarSystem != null)
            {
                dict["system"] = new ReflectionValue(EDDI.Instance.CurrentStarSystem);
            }

            if (EDDI.Instance.LastStarSystem != null)
            {
                dict["lastsystem"] = new ReflectionValue(EDDI.Instance.LastStarSystem);
            }

            if (EDDI.Instance.CurrentStation != null)
            {
                dict["station"] = new ReflectionValue(EDDI.Instance.CurrentStation);
            }

            if (theEvent != null)
            {
                dict["event"] = new ReflectionValue(theEvent);
            }

            if (EDDI.Instance.State != null)
            {
                Dictionary<Cottle.Value, Cottle.Value> state = new Dictionary<Cottle.Value, Cottle.Value>();
                foreach (string key in EDDI.Instance.State.Keys)
                {
                    object value = EDDI.Instance.State[key];
                    Type valueType = value.GetType();
                    if (valueType == typeof(string))
                    {
                        state[key] = (string)value;
                    }
                    else if (valueType == typeof(int))
                    {
                        state[key] = (int)value;
                    }
                    else if (valueType == typeof(bool))
                    {
                        state[key] = (bool)value;
                    }
                    else if (valueType == typeof(decimal))
                    {
                        state[key] = (decimal)value;
                    }
                }
                dict["state"] = state;
            }

            return dict;
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        private static readonly object logLock = new object();
        private static void log(string speech)
        {
            lock (logLock)
            {
                try
                {
                    using (StreamWriter file = new StreamWriter(LogFile, true))
                    {
                        file.WriteLine(speech);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to write speech", ex);
                }
            }
        }
    }
}
