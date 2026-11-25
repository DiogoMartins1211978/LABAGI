using System.Collections;
using UnityEngine;

public class CardAudioPlayer : MonoBehaviour
{
    public void PlaySequence(AudioSource source, AudioClip cardSound, AudioClip shinySound, int currentLevel)
    {
        StartCoroutine(PlaySequenceCoroutine(source, cardSound, shinySound, currentLevel));
    }

    private IEnumerator PlaySequenceCoroutine(AudioSource source, AudioClip cardSound, AudioClip shinySound, int currentLevel)
    {
        if (cardSound != null)
        {
            source.PlayOneShot(cardSound);
            yield return new WaitForSeconds(cardSound.length);
        }

        int shinyCount = Mathf.Max(0, currentLevel - 1);

        for (int i = 0; i < shinyCount; i++)
        {
            if (shinySound == null) break;

            source.PlayOneShot(shinySound);
            yield return new WaitForSeconds(shinySound.length);
        }

        Destroy(gameObject);
    }
}