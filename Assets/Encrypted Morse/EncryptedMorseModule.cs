﻿using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EncryptedMorseModule : MonoBehaviour {

    public KMBombInfo BombInfo;
    public KMBombModule BombModule;
    public KMAudio Audio;
    public KMSelectable MorseKnob, DotButton, DashButton, ResetButton, MorseSwitchWire;
    public MeshRenderer MorseLight, MorseSwitchWireRenderer;
    public Transform MorseKnobTransform;
    public Light MorseLightL;
    public Material MorseMessageMat, MorseKeyMat, LightOffMat, SolvedMat;
    public TextMesh BinText;

    private static Color messageMorseC = new Color(.9f, .45f, .05f);
    private static Color keyMorseC = new Color(.05f, .45f, .9f);
    private static Color solvedC = new Color(.1f, .9f, .05f);
    private static Color switchWireOff = new Color(.7f, 0, 0);
    private static Color switchWireOn = new Color(.36f, .52f, 1f);
    private static Color switchWireSolved = new Color(.1f, .7f, .1f);

    private static int lastLogNum = 0;
    private int logNum;
    private bool activated = false, solved = false;
    private bool morseEnabled = true;

    private ModSettings settings = new ModSettings("EncryptedMorse");

    private int morseFrameCounter;

    private int[] messageMorse, keyMorse, binStrA, binStrB;
    private int morsePos = -1;
    private int morseTick = 0;
    private bool keySelected = false;

    private static readonly string[] calls = { "DETONATE", "READYNOW", "WEREDEAD", "SHESELLS", "REMEMBER", "GREATJOB", "SOLOTHIS", "KEEPTALK" };
    private static readonly string[] responses = { "PLEASENO", "CHEESECAKE", "SADFACE", "SEASHELLS", "SOUVENIR", "THANKYOU", "IDAREYOU", "NOEXPLODE" };
    private const string solvedText = "YOU\nSOLVED\nTHIS\nMODULE\nGOOD\nJOB";
    private int callResponseIndex;
    private string call, response;
    private string message, key;
    private string encryptedCall;
    private int[] correctResponseMorse;
    private int currentResponseIndex = 0;

    #region Initialization & Updates
    // Use this for initialization
    void Start() {
        BombModule.OnActivate += OnActivate;
        MorseKnob.OnInteract += OnKnobPress;
        DotButton.OnInteract += OnDotPress;
        DashButton.OnInteract += OnDashPress;
        ResetButton.OnInteract += OnResetPress;
        MorseSwitchWire.OnInteract += OnMorseSwitch;
        logNum = ++lastLogNum;
        settings.ReadSettings();
        Init();
    }

    void Init() {
        morseFrameCounter = 0;
        ShowMorseMessage(false);

        binStrA = new int[42]; //start with random binStrA
        for (int i = 0; i < 42; i++) binStrA[i] = Random.value > .5 ? 1 : 0;

        string serial = "";
        List<string> data = BombInfo.QueryWidgets(KMBombInfo.QUERYKEY_GET_SERIAL_NUMBER, null);
        foreach (string response in data) {
            Dictionary<string, string> responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(response);
            serial = responseDict["serial"];
            break;
        }

        int batteries = 0;
        data = BombInfo.QueryWidgets(KMBombInfo.QUERYKEY_GET_BATTERIES, null);
        foreach (string response in data) {
            Dictionary<string, int> responseDict = JsonConvert.DeserializeObject<Dictionary<string, int>>(response);
            batteries += responseDict["numbatteries"];
        }

        int ports = 0;
        data = BombInfo.QueryWidgets(KMBombInfo.QUERYKEY_GET_PORTS, null);
        foreach (string response in data) {
            Dictionary<string, string[]> responseDict = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(response);
            foreach (string s in responseDict["presentPorts"]) ports++;
        }

        binStrB = new int[42]; //generate binStrB
        int offset = 0;
        foreach (char c in serial) {
            if (c >= 0x30 && c <= 0x39) { //digit
                for (int i = 0; i < 7; i++) {
                    binStrB[i + offset] = ((c - 0x30) & (1 << (6 - i))) > 0 ? 1 : 0; //binary representation, MSB first
                }
            } else if (c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U') { //vowel
                binStrB[offset] = 1;
                binStrB[offset + 1] = 0;
                binStrB[offset + 2] = 1;
                binStrB[offset + 3] = 1;
                binStrB[offset + 4] = 0;
                binStrB[offset + 5] = 0;
                binStrB[offset + 6] = 1;
            } else if (c < 0x4F) { //consonant before O
                binStrB[offset] = 1;
                binStrB[offset + 1] = 1;
                binStrB[offset + 2] = 0;
                binStrB[offset + 3] = 0;
                binStrB[offset + 4] = 0;
                binStrB[offset + 5] = 1;
                binStrB[offset + 6] = 0;
            } else { //consonant after O
                binStrB[offset] = 1;
                binStrB[offset + 1] = 0;
                binStrB[offset + 2] = 0;
                binStrB[offset + 3] = 0;
                binStrB[offset + 4] = 1;
                binStrB[offset + 5] = 0;
                binStrB[offset + 6] = 0;
            }
            offset += 7;
        }
        if (batteries == ports) { //reverse first half
            var rev = new int[21];
            for (int i = 0; i < 21; i++) rev[i] = binStrB[20 - i];
            rev.CopyTo(binStrB, 0);
        }

        callResponseIndex = Random.Range(0, calls.Length); //choose random call/response pair
        call = calls[callResponseIndex];
        response = responses[callResponseIndex];

        key = "";
        encryptedCall = "";
        for (int i = 0; i < call.Length; i++) { //encrypt call using (random) key string
            char keyChar = (char)(0x40 + Random.Range(1, 27)); //add random char to key
            key += keyChar;

            char encryptedChar = (char)(call[i] - (keyChar - 0x40)); //encrypt char
            while (encryptedChar < 0x41) encryptedChar += (char)26; //keep char in valid range
            encryptedCall += encryptedChar;
        }

        string logKey = key;
        string logEncryptedCall = encryptedCall; //save encCall and key for logging
        bool swapped = false;
        int numVowels = 0;
        foreach (char c in encryptedCall) if (c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U') numVowels++; //count vowels in encryptedCall
        if (numVowels > 1) { //if more than 1 vowel, swap first with fifth char and second with eighth char in call and key
            key = "" + key[4] + key[7] + key[2] + key[3] + key[0] + key[5] + key[6] + key[1];
            encryptedCall = "" + encryptedCall[4] + encryptedCall[7] + encryptedCall[2] + encryptedCall[3] 
                                + encryptedCall[0] + encryptedCall[5] + encryptedCall[6] + encryptedCall[1];
            swapped = true;
        }
        keyMorse = MorseEncode(key);

        int[] callMorse = MorseEncode(encryptedCall); //convert encrypted call to morse
        for (int i = 0; i < callMorse.Length; i++) { //modify morse
            if (callMorse[i] == 2) { //convert space
                if (binStrB[i] == 0) {
                    binStrA[i] = 0; //set A to satisfy space condition
                    callMorse[i] = 0; //turn space into dot
                } else {
                    binStrA[i] = 1; //set A to satisfy space condition
                    callMorse[i] = 1; //turn space into dash
                }
            } else if (binStrA[i] == 0 && binStrB[i] == 1) { //swap to other symbol if condition satisfied
                callMorse[i] = callMorse[i] == 0 ? 1 : 0;
            } else if (binStrB[i] == 0 && callMorse[i] == 0) { //make sure dots don't turn into spaces
                binStrA[i] = 1;
            } else if (binStrB[i] == 1 && callMorse[i] == 1) { //make sure dashes don't turn into spaces
                binStrA[i] = 0;
                callMorse[i] = 0; //set to dot because A,B now satisfy "swap" condition
            }
            //otherwise leave symbol alone
        }
        messageMorse = MorseDivide(callMorse, out message); //divide message into valid morse characters

        correctResponseMorse = MorseEncode(response, true); //convert correct response to morse, without letter spaces

        string binStrALog = "";
        string binStrADisp = "";
        for (int i = 0; i < 42; i++) {
            binStrALog += binStrA[i];
            binStrADisp += binStrA[i];
            if (i % 7 == 6 && i < 41) binStrADisp += '\n';
        }
        BinText.text = binStrADisp;

        DebugLog("Binary sequence A (received): " + binStrALog);
        if (batteries == ports) DebugLog("First half of generated binary sequence will be reversed (#batteries = #ports)");
        string binStrBLog = "";
        for (int i = 0; i < 42; i++) binStrBLog += binStrB[i];
        DebugLog("Binary sequence B (generated): " + binStrBLog);
        DebugLog("Received message: " + message);
        DebugLog("Received key: " + key);

        DebugLog("Intermediate message after binary decryption (first step): " + encryptedCall);
        if (swapped) {
            DebugLog("Intermediate message contains more than one vowel. Swapping message and key characters.");
            DebugLog("New intermediate message: " + logEncryptedCall);
            DebugLog("New key: " + logKey);
        }

        DebugLog("Final message after decryption: " + call);
        DebugLog("Correct response: " + response);
    }

    // Update is called once per frame
    void Update() {
        if (!activated) return;

        if (solved) {
            MorseLight.material = SolvedMat;
            MorseLightL.color = solvedC;
            MorseLightL.enabled = true;
            return;
        }

        if (!morseEnabled) {
            ShowMorseMessage(false);
        } else if (++morseFrameCounter * Time.deltaTime >= settings.Settings.morseTickTime) {
            morseFrameCounter = 0;

            int[] morse = keySelected ? keyMorse : messageMorse;

            if (morsePos < 0) {
                ShowMorseMessage(false);
                if (++morseTick > 12) {
                    morseTick = 0;
                    morsePos++;
                }
            } else {
                switch (morse[morsePos]) {
                    case 0:
                        if (++morseTick <= 1) {
                            if (keySelected) ShowMorseKey(true); else ShowMorseMessage(true);
                        } else {
                            ShowMorseMessage(false);
                            morseTick = 0;
                            morsePos++;
                        }
                        break;
                    case 1:
                        if (++morseTick <= 3) {
                            if (keySelected) ShowMorseKey(true); else ShowMorseMessage(true);
                        } else {
                            ShowMorseMessage(false);
                            morseTick = 0;
                            morsePos++;
                        }
                        break;
                    case 2:
                        ShowMorseMessage(false);
                        if (++morseTick > 3) {
                            morseTick = 0;
                            morsePos++;
                        }
                        break;
                }
                if (morsePos >= morse.Length) morsePos = -1;
            }
        }
    }

    void OnActivate() {
        activated = true;
    }
    #endregion

    #region Selectable Handlers
    bool OnKnobPress() {
        Audio.PlaySoundAtTransform("switch", transform);
        if (keySelected) {
            MorseKnobTransform.Rotate(0, 0, -100);
            keySelected = false;
        } else {
            MorseKnobTransform.Rotate(0, 0, 100);
            keySelected = true;
        }
        morsePos = -1;
        morseTick = 0;
        morseFrameCounter = 0;
        return false;
    }

    bool OnDotPress() {
        if (!activated) {
            BombModule.HandleStrike();
            return false;
        }

        if (solved) Audio.PlaySoundAtTransform("dot", transform);
        else if (correctResponseMorse[currentResponseIndex] == 0) {
            Audio.PlaySoundAtTransform("dot", transform);
            currentResponseIndex++;
            if (currentResponseIndex >= correctResponseMorse.Length) {
                DebugLog("Correct response entered, module defused.");
                SolveModule();
            }
        } else {
            DebugLog("Entered dot(.) as symbol #" + (currentResponseIndex + 1) + ", correct symbol is dash(-). Strike.");
            currentResponseIndex = 0;
            BombModule.HandleStrike();
        }

        return false;
    }

    bool OnDashPress() {
        if (!activated) {
            BombModule.HandleStrike();
            return false;
        }

        if (solved) Audio.PlaySoundAtTransform("dash", transform);
        else if (correctResponseMorse[currentResponseIndex] == 1) {
            Audio.PlaySoundAtTransform("dash", transform);
            currentResponseIndex++;
            if (currentResponseIndex >= correctResponseMorse.Length) {
                DebugLog("Correct response entered, module defused.");
                SolveModule();
            }
        } else {
            DebugLog("Entered dash(-) as symbol #" + (currentResponseIndex + 1) + ", correct symbol is dot(.). Strike.");
            currentResponseIndex = 0;
            BombModule.HandleStrike();
        }

        return false;
    }

    bool OnResetPress() {
        Audio.PlaySoundAtTransform("reset", transform);
        if (solved) return false;
        morsePos = -1;
        morseTick = 0;
        morseFrameCounter = 0;
        currentResponseIndex = 0;
        return false;
    }

    bool OnMorseSwitch() {
        Audio.PlaySoundAtTransform("switch", transform);
        if (solved) return false;
        if (morseEnabled) {
            MorseSwitchWireRenderer.material.color = switchWireOff;
            morseEnabled = false;
        } else {
            MorseSwitchWireRenderer.material.color = switchWireOn;
            morsePos = -1;
            morseTick = 0;
            morseFrameCounter = 0;
            morseEnabled = true;
        }
        return false;
    }
    #endregion

    #region Visual Methods
    void ShowMorseMessage(bool on) {
        MorseLight.material = on ? MorseMessageMat : LightOffMat;
        MorseLightL.color = messageMorseC;
        MorseLightL.enabled = on;
    }

    void ShowMorseKey(bool on) {
        MorseLight.material = on ? MorseKeyMat : LightOffMat;
        MorseLightL.color = keyMorseC;
        MorseLightL.enabled = on;
    }
    #endregion

    #region Logic & Utility Methods
    int[] MorseEncode(string s, bool noSpaces = false) {
        var mc = new List<int>();
        foreach (char c in s.ToUpperInvariant()) {
            switch (c) {
                case 'A':
                    mc.Add(0);
                    mc.Add(1);
                    break;
                case 'B':
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(0);
                    break;
                case 'C':
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(0);
                    break;
                case 'D':
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(0);
                    break;
                case 'E':
                    mc.Add(0);
                    break;
                case 'F':
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(0);
                    break;
                case 'G':
                    mc.Add(1);
                    mc.Add(1);
                    mc.Add(0);
                    break;
                case 'H':
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(0);
                    break;
                case 'I':
                    mc.Add(0);
                    mc.Add(0);
                    break;
                case 'J':
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(1);
                    mc.Add(1);
                    break;
                case 'K':
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(1);
                    break;
                case 'L':
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(0);
                    break;
                case 'M':
                    mc.Add(1);
                    mc.Add(1);
                    break;
                case 'N':
                    mc.Add(1);
                    mc.Add(0);
                    break;
                case 'O':
                    mc.Add(1);
                    mc.Add(1);
                    mc.Add(1);
                    break;
                case 'P':
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(1);
                    mc.Add(0);
                    break;
                case 'Q':
                    mc.Add(1);
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(1);
                    break;
                case 'R':
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(0);
                    break;
                case 'S':
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(0);
                    break;
                case 'T':
                    mc.Add(1);
                    break;
                case 'U':
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(1);
                    break;
                case 'V':
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(1);
                    break;
                case 'W':
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(1);
                    break;
                case 'X':
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(0);
                    mc.Add(1);
                    break;
                case 'Y':
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(1);
                    mc.Add(1);
                    break;
                case 'Z':
                    mc.Add(1);
                    mc.Add(1);
                    mc.Add(0);
                    mc.Add(0);
                    break;
            }
            if (!noSpaces) mc.Add(2);
        }
        if (!noSpaces) mc.RemoveAt(mc.Count - 1); //remove last unnecessary space
        return mc.ToArray();
    }

    int[] MorseDivide(int[] morse, out string message) {
        var ret = new List<int>();
        int index = 0;
        message = "";
        while (index < morse.Length) { //while there are symbols remaining
            int[] buf = new int[morse.Length - index]; //buffer all remaining symbols
            System.Array.ConstrainedCopy(morse, index, buf, 0, buf.Length);

            string letters;
            int charLen = Random.Range(1, MorseDivideMax(buf, out letters) + 1); //choose a random possible letter (length)
            message += letters[charLen - 1]; //save the chosen letter for logging

            for (int i = 0; i < charLen; i++) ret.Add(buf[i]);

            ret.Add(2);
            index += charLen;
        }
        return ret.ToArray();
    }

    // returns the maximum number of morse symbols that can be taken from the start of the given sequence to form a valid morse letter.
    // also returns those letters for logging purposes
    int MorseDivideMax(int[] morse, out string letters) {
        letters = "";
        if (morse[0] == 0) {
            letters += "E";
            if (morse.Length == 1) return 1;
            if (morse[1] == 0) {
                letters += "I";
                if (morse.Length == 2) return 2;
                if (morse[2] == 0) {
                    letters += "S";
                    if (morse.Length == 3) return 3;
                    if (morse[3] == 0) {
                        letters += "H";
                    } else {
                        letters += "V";
                    }
                    return 4;
                } else {
                    letters += "U";
                    if (morse.Length == 3 || morse[3] == 1) return 3; //short or invalid ..--
                    letters += "F";
                    return 4;
                }
            } else {
                letters += "A";
                if (morse.Length == 2) return 2;
                if (morse[2] == 0) {
                    letters += "R";
                    if (morse.Length == 3 || morse[3] == 1) return 3; //short or invalid .-.-
                    letters += "L";
                    return 4;
                } else {
                    letters += "W";
                    if (morse.Length == 3) return 3;
                    if (morse[3] == 0) {
                        letters += "P";
                    } else {
                        letters += "J";
                    }
                    return 4;
                }
            }
        } else {
            letters += "T";
            if (morse.Length == 1) return 1;
            if (morse[1] == 0) {
                letters += "N";
                if (morse.Length == 2) return 2;
                if (morse[2] == 0) {
                    letters += "D";
                    if (morse.Length == 3) return 3;
                    if (morse[3] == 0) {
                        letters += "B";
                    } else {
                        letters += "X";
                    }
                    return 4;
                } else {
                    letters += "K";
                    if (morse.Length == 3) return 3;
                    if (morse[3] == 0) {
                        letters += "C";
                    } else {
                        letters += "Y";
                    }
                    return 4;
                }
            } else {
                letters += "M";
                if (morse.Length == 2) return 2;
                if (morse[2] == 0) {
                    letters += "G";
                    if (morse.Length == 3) return 3;
                    if (morse[3] == 0) {
                        letters += "Z";
                    } else {
                        letters += "Q";
                    }
                    return 4;
                } else {
                    letters += "O";
                    return 3;
                }
            }
        }
    }

    void SolveModule() {
        solved = true;
        MorseSwitchWireRenderer.material.color = switchWireSolved;
        BinText.text = solvedText;
        BombModule.HandlePass();
    }

    void DebugLog(string message) {
        Debug.Log("[Encrypted Morse #" + logNum + "] " + message);
    }
    #endregion

    #region TwitchPlays

    #pragma warning disable 0414
    string TwitchHelpMessage = "Transmit a response using 'submit .--...-'. Toggle the knob using 'toggle knob'. Toggle the morse light using 'toggle morse'. Press the reset button using 'reset'.";
    #pragma warning restore 0414

    public void TwitchHandleForcedSolve() {
        DebugLog("Module solved by TP command.");
        SolveModule();
    }

    public KMSelectable[] ProcessTwitchCommand(string cmd) {
        cmd = cmd.ToLowerInvariant().Trim();
        if (cmd == "reset") return new KMSelectable[] { ResetButton };
        else if (cmd.StartsWith("submit")) {
            if (cmd.Length <= 7) throw new System.FormatException("Invalid command: '" + cmd + "'");
            var buttons = new List<KMSelectable>();
            foreach (char c in cmd.Substring(7)) {
                if (c == '.') buttons.Add(DotButton);
                else if (c == '-') buttons.Add(DashButton);
                else throw new System.FormatException("Invalid morse character: '" + c + "'");
            }
            return buttons.ToArray();
        } else if (cmd.StartsWith("toggle")) {
            if (cmd.Length <= 7) throw new System.FormatException("Invalid command: '" + cmd + "'");
            switch (cmd.Substring(7)) {
                case "knob": return new KMSelectable[] { MorseKnob };
                case "morse": return new KMSelectable[] { MorseSwitchWire };
                default: throw new System.FormatException("Invalid command: '" + cmd + "'");
            }
        } else throw new System.FormatException("Invalid command: '" + cmd + "'");
    }
    #endregion
}
