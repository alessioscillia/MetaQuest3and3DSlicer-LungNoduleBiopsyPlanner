// This code is based on the one provided in: https://github.com/franklinwk/OpenIGTLink-Unity
// Modified by Alicia Pose Díez de la Lastra, from Universidad Carlos III de Madrid

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;



public class OpenIGTLinkConnect : MonoBehaviour
{
    ///////// CONNECT TO 3D SLICER PARAMETERS /////////
    uint headerSize = 58; // Size of the header of every OpenIGTLink message
    private SocketHandler socketForUnityAndMetaQuest; // Socket to connect to Slicer
    bool isConnected; // Boolean to check if the socket is connected
    public string ipString; // IP address of the computer running Slicer
    public int port; // Port of the computer running Slicer
    public bool connectOnStart = true; // Connect automatically on Start
    ///////// PRE-ALLOCATED BUFFERS FOR IMAGES /////////
    private byte[] _cachedImData;
    private byte[] _cachedRGBData;

    [Header("Slicer Calibration")]
    [Tooltip("Offset spaziale inviato a Slicer per compensare il crop di TotalSegmentator.")]
    public Vector3 slicerPositionOffset = Vector3.zero;

    ///////// GENERAL VARIABLES /////////
    int scaleMultiplier = 1000; // Help variable to transform meters to millimeters and vice versa


    ///////// SEND /////////
    public List<ModelInfo> infoToSend; // Array of Models to send to Slicer

    /// CRC ECMA-182 to send messages to Slicer ///
    CRC64 crcGenerator;
    string CRC;
    ulong crcPolynomial;
    string crcPolynomialBinary = "0100001011110000111000011110101110101001111010100011011010010011";


    ///////// LISTEN /////////

    /// Image transfer information ///
    public GameObject movingPlane; // Plane to display image on
    Material mediaMaterial; // Material of the plane
    Texture2D mediaTexture; // Texture of the plane

    public GameObject fixPlane; // Fix plane to display image on
    Material fixPlaneMaterial; // Material of the plane


    /// <summary>
    /// Imposta il piano mobile che riceverà le immagini dalle slice
    /// </summary>
    public void SetMovingPlane(GameObject plane)
    {
        movingPlane = plane;
        if (movingPlane != null)
        {
            // Nessuna inversione della scala Y
            mediaMaterial = movingPlane.GetComponent<MeshRenderer>().material;
                        
            // Se la texture a colori è già stata creata (dal Fixed Plane), usiamo subito quella
            if (mediaTexture != null)
            {
                mediaMaterial.mainTexture = mediaTexture;
                // Sicurezza aggiuntiva per URP: forziamo la BaseMap
                mediaMaterial.SetTexture("_BaseMap", mediaTexture);
            }
            
            // Forza le impostazioni texture per coprire tutto il piano
            mediaMaterial.mainTextureScale = Vector2.one;
            mediaMaterial.mainTextureOffset = Vector2.zero;
            mediaMaterial.SetTextureScale("_BaseMap", Vector2.one);
            mediaMaterial.SetTextureOffset("_BaseMap", Vector2.zero);
        }
    }

    void Start()
    {
        // Initialize CRC Generator
        crcGenerator = new CRC64();
        crcPolynomial = Convert.ToUInt64(crcPolynomialBinary, 2);
        crcGenerator.Init(crcPolynomial);

        // Initialize texture parameters for image transfer of the moving plane
        // Solo se movingPlane è già stato assegnato nell'Inspector
        if (movingPlane != null)
        {
            SetMovingPlane(movingPlane);
        }

        // Initialize texture parameters for image transfer of the fix plane
        GameObject fixedImagePlane = GameObject.Find("FixedImagePlane");
        if (fixedImagePlane != null)
        {
            fixPlane = fixedImagePlane.transform.Find("FixPlane").gameObject;
            if (fixPlane != null)
            {
                // Nessuna inversione della scala Y
                fixPlaneMaterial = fixPlane.GetComponent<MeshRenderer>().material;
                
                // Forza le impostazioni texture per coprire tutto il piano
                fixPlaneMaterial.mainTextureScale = Vector2.one;
                fixPlaneMaterial.mainTextureOffset = Vector2.zero;
                
                if (mediaTexture != null)
                {
                    fixPlaneMaterial.mainTexture = mediaTexture;
                }
            }
        }

        if (connectOnStart)
        {
            OnConnectToSlicerClick(ipString, port);
        }
    }

    // This function is called when the user activates the connectivity switch to start the communication with 3D Slicer
    public bool OnConnectToSlicerClick(string ipString, int port)
    {
        isConnected = ConnectToSlicer(ipString, port);
        return isConnected;
    }

    // Create a new socket handler and connect it to the server with the ip address and port provided in the function
    bool ConnectToSlicer(string ipString, int port)
    {
        socketForUnityAndMetaQuest = new SocketHandler();

        Debug.Log("ipString: " + ipString);
        Debug.Log("port: " + port);
        bool isConnected = socketForUnityAndMetaQuest.Connect(ipString, port);
        Debug.Log("Connected: " + isConnected);

        if (isConnected)
        {
            StartCoroutine(ListenSlicerInfo());
            StartCoroutine(SendTransformInfo());
        }

        return isConnected;

    }

    // Routine that continuously sends the transform information of every model in infoToSend to 3D Slicer
    public IEnumerator SendTransformInfo()
    {
        while (true)
        {
            yield return null; // If you had written yield return new WaitForSeconds(1); it would have waited 1 second before executing the code below.
            // Loop foreach element in infoToSend
            foreach (ModelInfo element in infoToSend)
            {
                SendMessageToServer.SendTransformMessage(element, scaleMultiplier, crcGenerator, CRC, socketForUnityAndMetaQuest, slicerPositionOffset);
            }
        }
    }

    // Routine that continuously listents to the incoming information from 3D Slicer. In the present code, this information could be in the form of a transform or an image message
    public IEnumerator ListenSlicerInfo()
    {
        while (true)
        {
            yield return null;

            ////////// READ THE HEADER OF THE INCOMING MESSAGES //////////
            byte[] iMSGbyteArray = socketForUnityAndMetaQuest.Listen(headerSize);

            // SE IL SOCKET RITORNA NULL, SALTA AL PROSSIMO FRAME!
            if (iMSGbyteArray == null) 
                continue;


            if (iMSGbyteArray.Length >= (int)headerSize)
            {
                ////////// READ THE HEADER OF THE INCOMING MESSAGES //////////
                // Store the information of the header in the structure iHeaderInfo
                ReadMessageFromServer.HeaderInfo iHeaderInfo = ReadMessageFromServer.ReadHeaderInfo(iMSGbyteArray);

                ////////// READ THE BODY OF THE INCOMING MESSAGES //////////
                // Get the size of the body from the header information
                // Verifica che bodySize non sia troppo grande (max 100MB per sicurezza)
                if (iHeaderInfo.bodySize > 100 * 1024 * 1024)
                {
                    Debug.LogWarning($"[OpenIGTLink] Messaggio troppo grande ricevuto: {iHeaderInfo.bodySize} bytes. Scartato.");
                    continue;
                }
                
                uint bodySize = (uint)iHeaderInfo.bodySize;

                // Process the message when it is complete (that means, we have received as many bytes as the body size + the header size)
                if (iMSGbyteArray.Length >= (int)bodySize + (int)headerSize)
                {
                    // Compare different message types and act accordingly
                    if (iHeaderInfo.msgType.Contains("IMAGE"))
                    {
                        // Read and apply the image content to our preview plane
                        ApplyImageInfo(iMSGbyteArray, iHeaderInfo);
                    }
                    else if (iHeaderInfo.msgType.Contains("STATUS"))
                    {
                        // STATUS message (keepalive)
                    }
                    else
                    {
                        Debug.LogWarning($"Unknown or unhandled message type: {iHeaderInfo.msgType}");
                    }
                }
            }
        }
    }

//////////////////////////////// INCOMING IMAGE MESSAGE ////////////////////////////////
    void ApplyImageInfo(byte[] iMSGbyteArray, ReadMessageFromServer.HeaderInfo iHeaderInfo)
    {
        ReadMessageFromServer.ImageInfo iImageInfo = ReadMessageFromServer.ReadImageInfo(iMSGbyteArray, headerSize, iHeaderInfo.extHeaderSize);
        
        if (iImageInfo.numPixX > 0 && iImageInfo.numPixY > 0)
        {
            if (movingPlane == null) return;

            // --- 1. SETUP TEXTURE (Standard) ---
            mediaMaterial = movingPlane.GetComponent<MeshRenderer>().material;
            
            if (mediaTexture == null || mediaTexture.width != iImageInfo.numPixX || mediaTexture.height != iImageInfo.numPixY)
            {
                 mediaTexture = new Texture2D(iImageInfo.numPixX, iImageInfo.numPixY, TextureFormat.RGB24, false);
                 mediaTexture.wrapMode = TextureWrapMode.Clamp; 
                 mediaTexture.filterMode = FilterMode.Bilinear;
            }

            if (fixPlane != null)
                fixPlaneMaterial = fixPlane.GetComponent<MeshRenderer>().material;

            // --- 2. CARICAMENTO PIXEL (Non-Allocating) ---
            int totalPixels = iImageInfo.numPixX * iImageInfo.numPixY;

            // Inizializza i buffer SOLO la prima volta o se la risoluzione di Slicer cambia
            if (_cachedImData == null || _cachedImData.Length != totalPixels)
            {
                _cachedImData = new byte[totalPixels];
                _cachedRGBData = new byte[totalPixels * 3];
            }

            // Copia i dati dal messaggio in arrivo nel buffer pre-esistente
            Buffer.BlockCopy(iMSGbyteArray, iImageInfo.offsetBeforeImageContent, _cachedImData, 0, totalPixels);

            // Processa i pixel trasformando scala di grigi in RGB
            for (int i = 0; i < totalPixels; i++)
            {
                byte pixelVal = _cachedImData[i];
                _cachedRGBData[i * 3]     = pixelVal;
                _cachedRGBData[i * 3 + 1] = pixelVal;
                _cachedRGBData[i * 3 + 2] = pixelVal;
            }

            mediaTexture.LoadRawTextureData(_cachedRGBData);
            mediaTexture.Apply();
            
            // --- 3. GESTIONE GEOMETRIA: FORZIAMO IL QUADRATO ---
            // Ignoriamo le dimensioni dell'immagine. Usiamo la Scale Y impostata in Unity per fare un quadrato.
            
            // A. Moving Plane (Quadrato)
            float size = Mathf.Abs(movingPlane.transform.localScale.y);
            float signX = Mathf.Sign(movingPlane.transform.localScale.x); // Mantiene orientamento
            movingPlane.transform.localScale = new Vector3(size * signX, size, movingPlane.transform.localScale.z);

            // B. Fix Plane (Quadrato)
            if (fixPlane != null)
            {
                float fixSize = Mathf.Abs(fixPlane.transform.localScale.y);
                fixPlane.transform.localScale = new Vector3(fixSize, fixSize, fixPlane.transform.localScale.z);
            }

            // --- 4. CALCOLO UV PER "ZOOM & CROP" (Riempi il quadrato) ---
            // Calcoliamo i rapporti d'aspetto
            float imageAspect = (float)iImageInfo.numPixX / (float)iImageInfo.numPixY; // Es. 1.86 (Larga)
            float planeAspect = 1.0f; // Poiché abbiamo forzato il quadrato qui sopra

            // Fattore di scala per la texture
            Vector2 scaleUV = Vector2.one;
            Vector2 offsetUV = Vector2.zero;

            // Se l'immagine è più larga del piano (il tuo caso: 1.86 > 1.0)
            if (imageAspect > planeAspect)
            {
                // Dobbiamo mostrare solo la parte centrale orizzontale
                float scaleFactor = planeAspect / imageAspect; // Es. 1.0 / 1.86 = 0.53
                scaleUV.x = scaleFactor;
                scaleUV.y = 1;
                
                // Centriamo la texture (crop laterale)
                offsetUV.x = (1 - scaleFactor) / 2;
                offsetUV.y = 0;
            }
            // Se l'immagine è più alta del piano
            else
            {
                // Dobbiamo mostrare solo la parte centrale verticale
                float scaleFactor = imageAspect / planeAspect;
                scaleUV.x = 1;
                scaleUV.y = scaleFactor;
                
                // Centriamo la texture (crop verticale)
                offsetUV.x = 0;
                offsetUV.y = (1 - scaleFactor) / 2;
            }

            // --- 5. APPLICAZIONE AI MATERIALI ---
            
            // PIANO MOBILE
            mediaMaterial.mainTexture = mediaTexture;
            mediaMaterial.SetTexture("_BaseMap", mediaTexture); // Forzatura URP
            
            if (imageAspect > planeAspect) 
            {
                float scaleFactor = planeAspect / imageAspect;
                mediaMaterial.mainTextureScale = new Vector2(scaleFactor, -1);
                mediaMaterial.mainTextureOffset = new Vector2(offsetUV.x, 1); 
                
                // Forzatura URP per Zoom & Crop
                mediaMaterial.SetTextureScale("_BaseMap", new Vector2(scaleFactor, -1));
                mediaMaterial.SetTextureOffset("_BaseMap", new Vector2(offsetUV.x, 1));
            }
            else
            {
                mediaMaterial.mainTextureScale = new Vector2(1, -scaleUV.y);
                mediaMaterial.mainTextureOffset = new Vector2(0, 1 - offsetUV.y);
                
                // Forzatura URP per Zoom & Crop
                mediaMaterial.SetTextureScale("_BaseMap", new Vector2(1, -scaleUV.y));
                mediaMaterial.SetTextureOffset("_BaseMap", new Vector2(0, 1 - offsetUV.y));
            }

            // PIANO FISSO
            if (fixPlaneMaterial != null)
            {
                fixPlaneMaterial.mainTexture = mediaTexture;
                
                if (imageAspect > planeAspect)
                {
                    float scaleFactor = planeAspect / imageAspect;
                    // Questo non aveva il mirroring prima. Per ribaltarlo orizzontalmente, mettiamo il meno alla X.
                    fixPlaneMaterial.mainTextureScale = new Vector2(-scaleFactor, 1);
                    fixPlaneMaterial.mainTextureOffset = new Vector2(1 - offsetUV.x, 0);
                }
                else
                {
                    // Ribaltiamo l'asse X mettendolo a -1.
                    fixPlaneMaterial.mainTextureScale = new Vector2(-1, 1);
                    fixPlaneMaterial.mainTextureOffset = new Vector2(1, 0);
                }
            }
        }
    }


    // Called when the user disconnects Unity from 3D Slicer using the connectivity switch
    public void OnDisconnectClick()
    {
        StopAllCoroutines();
        socketForUnityAndMetaQuest.Disconnect();
        Debug.Log("Disconnected from the server");
    }


    // Execute this function when the user exits the application
    void OnApplicationQuit()
    {
        // Release the socket.
        if (socketForUnityAndMetaQuest != null)
        {
            socketForUnityAndMetaQuest.Disconnect();
        }
    }

    // Aggiungi questo metodo dentro la classe OpenIGTLinkConnect
// In OpenIGTLinkConnect.cs

public void RegisterDynamicModel(GameObject objToSend, string nameInSlicer)
{
    // Se la lista non è inizializzata, creala
    if (infoToSend == null) infoToSend = new List<ModelInfo>();

    // Controlla se esiste già per evitare duplicati
    foreach (var model in infoToSend)
    {
        // CORREZIONE QUI: Usa _gameObject invece di gameObject
        if (model._gameObject == objToSend) return;
    }

    // Crea il nuovo info
    ModelInfo newInfo = new ModelInfo();
    
    // CORREZIONI QUI: Usa le variabili col trattino basso come definite nella tua classe ModelInfo
    newInfo._gameObject = objToSend;
    newInfo._name = nameInSlicer; 
    newInfo._color = "White"; // Impostiamo un colore di default per evitare problemi
    
    // Aggiungilo alla lista di invio
    infoToSend.Add(newInfo);
    
    Debug.Log($"[OpenIGTLink] Registrato modello dinamico da inviare: {nameInSlicer}");
}
}
