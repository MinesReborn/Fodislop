"use strict";

console.log("[FMOD Setup] Linking audio files to events...");

function findAssetByName(folder, name) {
    var items = folder.items;
    if (!items) return null;
    for (var i = 0; i < items.length; i++) {
        var item = items[i];
        if (item.isOfType("AudioFile") && item.assetPath.indexOf(name) !== -1) {
            return item;
        }
        if (item.isOfType("AssetFolder")) {
            var found = findAssetByName(item, name);
            if (found) return found;
        }
    }
    return null;
}

var masterAssetFolder = studio.project.workspace.masterAssetFolder;

function addSoundToEvent(eventPath, wavFileName, isLoop) {
    var evt = studio.project.lookup(eventPath);
    if (!evt) {
        console.log("[FMOD Setup] Event not found:", eventPath);
        return;
    }

    var audioFile = findAssetByName(masterAssetFolder, wavFileName);
    if (!audioFile) {
        console.log("[FMOD Setup] AudioFile asset not found for:", wavFileName);
        return;
    }

    // Check if sound instrument already exists on timeline
    var track = evt.addGroupTrack("Audio");
    var sound = track.addSound(evt.timeline, "SingleSound", 0, audioFile.length || 2.0);
    sound.audioFile = audioFile;

    if (isLoop) {
        var loopRegion = evt.timeline.addRegion(0, audioFile.length || 2.0, "LoopRegion");
    }

    console.log("[FMOD Setup] Successfully linked", wavFileName, "to", eventPath);
}

addSoundToEvent("event:/sfx/bz", "bz.wav", false);
addSoundToEvent("event:/sfx/hurt", "hurt.wav", false);
addSoundToEvent("event:/sfx/death", "death.wav", false);
addSoundToEvent("event:/sfx/destroy", "destroy.wav", false);
addSoundToEvent("event:/music/ambient_bg", "evil_huge.wav", true);

console.log("[FMOD Setup] Audio linking complete!");
