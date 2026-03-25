using System.Collections;
using UnityEngine;

public class DelayedActivator : MonoBehaviour
{
    [Tooltip("Trascina qui l'oggetto ISDK_RayGrabInteraction che hai disattivato nella gerarchia")]
    public GameObject rayGrabInteractionObject;
    
    [Tooltip("Secondi da aspettare per far stabilizzare il simulatore Meta")]
    public float delay = 1.0f;

    void Start()
    {
        if (rayGrabInteractionObject != null)
        {
            StartCoroutine(ActivateAfterDelay());
        }
        else
        {
            Debug.LogWarning("[DelayedActivator] Non hai assegnato l'oggetto da attivare!");
        }
    }

    IEnumerator ActivateAfterDelay()
    {
        // Aspettiamo che il simulatore XR abbia caricato le mani/controller
        yield return new WaitForSeconds(delay);
        
        // Accendiamo l'oggetto. Poiché era già configurato nell'Editor, 
        // farà il suo Awake/Start in modo pulito e con tutti i riferimenti intatti!
        rayGrabInteractionObject.SetActive(true);
        
        Debug.Log("[DelayedActivator] Interazione attivata con successo!");
    }
}