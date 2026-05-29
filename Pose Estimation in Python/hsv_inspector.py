import cv2 as cv
import numpy as np
import sys
import os

# Variabili globali per condividere i dati con la funzione del mouse
img_bgr = None
img_hsv = None
window_name = "HSV Inspector (Premi ESC per uscire)"

def mouse_callback(event, x, y, flags, param):
    """Funzione chiamata automaticamente da OpenCV quando muovi il mouse"""
    if event == cv.EVENT_MOUSEMOVE:
        # Assicurati che le coordinate del mouse siano dentro i limiti dell'immagine
        if y < img_hsv.shape[0] and x < img_hsv.shape[1]:
            # Prendi i valori H, S, V dal pixel sotto il puntatore
            h, s, v = img_hsv[y, x]
            
            # Crea una copia fresca dell'immagine per disegnare l'interfaccia 
            # senza rovinare l'immagine originale
            display_img = img_bgr.copy()
            
            # Formatta il testo
            text = f"X:{x} Y:{y} | H:{h} S:{s} V:{v}"
            
            # Disegna uno sfondo nero semi-trasparente in alto a sinistra per leggere bene
            cv.rectangle(display_img, (10, 10), (450, 50), (0, 0, 0), -1)
            
            # Scrivi i valori HSV in bianco
            cv.putText(display_img, text, (20, 35), cv.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
            
            # Disegna un quadratino che mostra il colore esatto che stai puntando
            b, g, r = img_bgr[y, x]
            cv.rectangle(display_img, (400, 15), (440, 45), (int(b), int(g), int(r)), -1)
            cv.rectangle(display_img, (400, 15), (440, 45), (255, 255, 255), 1) # Bordo bianco

            # Aggiorna la finestra
            cv.imshow(window_name, display_img)

def main():
    global img_bgr, img_hsv
    
    # Se non specifichi un'immagine, prova ad aprire quella di default del Quest
    filename = "debug_01_original_from_quest.jpg"
    
    # Se passi un argomento da terminale, usa quello
    if len(sys.argv) > 1:
        filename = sys.argv[1]
        
    if not os.path.exists(filename):
        print(f"Errore: Il file '{filename}' non esiste.")
        print("Uso: python hsv_inspector.py [nome_immagine.jpg]")
        return

    # Carica l'immagine originale (BGR)
    img_bgr = cv.imread(filename)
    if img_bgr is None:
        print("Errore: Impossibile leggere l'immagine (forse è corrotta).")
        return

    # Converti in HSV una volta sola all'avvio per risparmiare calcoli
    img_hsv = cv.cvtColor(img_bgr, cv.COLOR_BGR2HSV)

    # Crea la finestra e collega il sensore del mouse
    cv.namedWindow(window_name)
    cv.setMouseCallback(window_name, mouse_callback)

    # Mostra l'immagine iniziale
    cv.imshow(window_name, img_bgr)
    
    print("==================================================")
    print("🟢 INSPECTOR AVVIATO")
    print(f"File caricato: {filename}")
    print("Muovi il mouse sull'immagine per esplorare i pixel.")
    print("Premi 'ESC' o 'q' sulla tastiera per chiudere.")
    print("==================================================")
    
    # Loop infinito per mantenere aperta la finestra
    while True:
        key = cv.waitKey(1) & 0xFF
        if key == 27 or key == ord('q'): # 27 è il tasto ESC
            break
            
    cv.destroyAllWindows()

if __name__ == '__main__':
    main()