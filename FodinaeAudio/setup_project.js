"use strict";

var masterBus = studio.project.workspace.mixer.masterBus;

function getOrCreateGroup(name) {
    var groups = masterBus.mixerGroup;
    if (groups) {
        for (var i = 0; i < groups.length; i++) {
            if (groups[i].name === name) return groups[i];
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

console.log("[FMOD Setup] Created mixer groups: sfx, music, voice, ambience, ui");

var masterFolder = studio.project.workspace.masterEventFolder;

function getOrCreateFolder(parentFolder, name) {
    var items = parentFolder.items;
    if (items) {
        for (var i = 0; i < items.length; i++) {
            if (items[i].name === name && items[i].isOfType("EventFolder")) {
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

console.log("[FMOD Setup] Created event folders: sfx, music");

// Master Bank
var masterBank = studio.project.workspace.masterBankFolder.items[0];

function createEvent(folder, name, bus) {
    var evt = studio.project.create("Event");
    evt.name = name;
    evt.folder = folder;
    if (bus) {
        evt.masterTrack.mixerGroup.output = bus;
    }
    if (masterBank) {
        evt.relationships.banks.add(masterBank);
    }
    console.log("[FMOD Setup] Created event: " + name);
    return evt;
}

createEvent(sfxFolder, "bz", sfxBus);
createEvent(sfxFolder, "hurt", sfxBus);
createEvent(sfxFolder, "death", sfxBus);
createEvent(sfxFolder, "destroy", sfxBus);
createEvent(musicFolder, "ambient_bg", musicBus);

console.log("[FMOD Setup] All events created and assigned to Master bank!");
