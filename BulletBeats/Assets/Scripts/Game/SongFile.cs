using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Reads a wav, mp3, or ogg from disk into an <see cref="AudioClip"/> the chart builder can sample.
/// </summary>
public static class SongFile
{
    public static AudioType AudioTypeFor(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            default: return AudioType.WAV;
        }
    }

    public static IEnumerator Load(string path, Action<AudioClip> onLoaded)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError($"SongFile: missing file '{path}'.");
            yield break;
        }

        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioTypeFor(path)))
        {
            var handler = (DownloadHandlerAudioClip)request.downloadHandler;
            handler.streamAudio = false;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"SongFile: could not load '{path}' ({request.error}).");
                yield break;
            }

            AudioClip source = DownloadHandlerAudioClip.GetContent(request);
            if (source == null)
            {
                Debug.LogError($"SongFile: '{path}' decoded to no audio.");
                yield break;
            }

            if (source.loadState == AudioDataLoadState.Unloaded)
                source.LoadAudioData();
            while (source.loadState == AudioDataLoadState.Loading)
                yield return null;

            int channels = Mathf.Max(1, source.channels);
            var data = new float[source.samples * channels];
            if (source.samples <= 0 || !source.GetData(data, 0))
            {
                Debug.LogError($"SongFile: '{path}' has no readable samples.");
                yield break;
            }

            // The downloaded clip dies with the request. Keep an independent copy.
            AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), source.samples, channels, source.frequency, false);
            clip.SetData(data, 0);
            onLoaded?.Invoke(clip);
        }
    }
}
