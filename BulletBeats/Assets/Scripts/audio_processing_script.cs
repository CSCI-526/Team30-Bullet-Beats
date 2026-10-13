using UnityEngine;

// [RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(AudioListener))]

public class audio_processing_script : MonoBehaviour
{
    AudioSource m_MyAudioSource;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_MyAudioSource = GetComponent<AudioSource>();
        
    }

    // Update is called once per frame
    void Update()
    {
        float[] spectrum = new float[256];

        AudioListener.GetSpectrumData(spectrum, 0, FFTWindow.Rectangular);
        // float mean = 0;
        // for(int i = 0; i < spectrum.Length; i++){
        //     mean = mean + spectrum[i];
        // }
        // mean = mean/spectrum.Length;
        // Debug.Log("Spectrum: " + string.Join(", ", spectrum));
        // Debug.Log("Spectrum Mean: " + mean);

        
        Vector3 graphOrigin = new Vector3(4.6294f, 8.17f, -46.4f);
        float width = 30f; // Width of the graph in world units
        for (int i = 1; i < spectrum.Length; i++)
        {
            float x0 = (i - 1) * width / (spectrum.Length - 1);
            float x1 = i * width / (spectrum.Length - 1);

            Debug.DrawLine(
                graphOrigin + new Vector3(x0, spectrum[i - 1] * 5f, 0),
                graphOrigin + new Vector3(x1, spectrum[i] * 5f, 0),
                Color.red);
        }


        
    }
}
