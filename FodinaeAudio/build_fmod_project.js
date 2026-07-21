"use strict";

console.log("=== FMOD STUDIO FULL BUILD SCRIPT ===");

// 1. Mixer Buses
var masterBus = studio.project.workspace.mixer.masterBus;

function getOrCreateGroup(name) {
    var groups = masterBus.input || masterBus.mixerGroup || [];
    for (var i = 0; i < groups.length; i++) {
        if (groups[i].name === name) {
            return groups[i];
        }
    }
    // Search model objects directly if array property differs in FMOD Studio version
    var allGroups = studio.project.model.MixerGroup.findInstances();
    for (var j = 0; j < allGroups.length; j++) {
        if (allGroups[j].name === name) {
            return allGroups[j];
        }
    }
    var group = studio.project.create("MixerGroup");
    group.name = name;
    group.output = masterBus;
    return group;
}

var sfxBus      = getOrCreateGroup("sfx");
var musicBus    = getOrCreateGroup("music");
var voiceBus    = getOrCreateGroup("voice");
var ambienceBus = getOrCreateGroup("ambience");
var uiBus       = getOrCreateGroup("ui");

// 2. Event Folders
var masterFolder = studio.project.workspace.masterEventFolder;

function getOrCreateFolder(parentFolder, name) {
    var items = parentFolder.items;
    if (items) {
        for (var i = 0; i < items.length; i++) {
            if (items[i].name === name && (items[i].isOfType("EventFolder") || items[i].isOfType("Folder"))) {
                return items[i];
            }
        }
    }
    var folder = studio.project.create("EventFolder");
    folder.name = name;
    folder.folder = parentFolder;
    return folder;
}

var sfxFolder   = getOrCreateFolder(masterFolder, "sfx");
var musicFolder = getOrCreateFolder(masterFolder, "music");

// 3. Master Bank
var masterBank = studio.project.workspace.masterBankFolder.items[0];

function createEventWithAudio(folder, name, bus, relWavPath) {
    var existing = folder.items;
    var evt = null;
    if (existing) {
        for (var i = 0; i < existing.length; i++) {
            if (existing[i].name === name && existing[i].isOfType("Event")) {
                evt = existing[i];
                break;
            }
        }
    }

    if (!evt) {
        evt = studio.project.create("Event");
        evt.name = name;
        evt.folder = folder;
    }

    if (bus) {
        evt.masterTrack.mixerGroup.output = bus;
    }

    if (masterBank) {
        evt.relationships.banks.add(masterBank);
    }

    // Import audio file asset
    var audioFile = studio.project.importAudioFile(relWavPath);
    if (audioFile) {
        var track = evt.addGroupTrack("AudioTrack");
        var sound = track.addSound(evt.timeline, "SingleSound", 0, audioFile.length || 2.0);
        sound.audioFile = audioFile;
        console.log("[FMOD Build] Event created & audio linked:", name, "->", relWavPath);
    }

    return evt;
}

// Create events in sfx folder (e.g. event:/sfx/bz)
createEventWithAudio(sfxFolder, "bz", sfxBus, "Sfx/bz.wav");
createEventWithAudio(sfxFolder, "hurt", sfxBus, "Sfx/hurt.wav");
createEventWithAudio(sfxFolder, "death", sfxBus, "Sfx/death.wav");
createEventWithAudio(sfxFolder, "destroy", sfxBus, "Sfx/destroy.wav");

// Create music event
createEventWithAudio(musicFolder, "ambient_bg", musicBus, "Music/evil_huge.wav");


// Save metadata to disk!
studio.project.save();
console.log("[FMOD Build] Studio project saved to disk!");
