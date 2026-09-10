using System;
using System.Collections.Generic;
using System.Text;

namespace image_flip_bosch.Bot.ImgFlip
{
  internal interface IImgFlipApi
  {
    /*
     * Liefert ein Array beliebter Meme-Vorlagen. Anzahl, 
     * Reihenfolge (nach Beliebtheit der letzten 30 Tage sortiert) 
     * und JSON-Felder können sich dynamisch ändern.
     */
    Meme[] GetMemes();
    /*
     * Fügt einer Imgflip-Meme-Vorlage Text hinzu. 
     * Erstellte Memes sind per Direkt-URL öffentlich abrufbar (kein privater Modus), 
     * werden jedoch nicht öffentlich gelistet. 
     * Selten aufgerufene Bilder werden nach einiger Zeit automatisch gelöscht.
     */
    ResponseImgFlip CaptionImage(string templateId, string username,string password,string text0,string text1,int? maxFontSize = null, bool noWatermark = false, MemeCreationBox[]? boxes = null);
    /*
     * Fügt einer animierten GIF-Vorlage Text hinzu. 
     * Funktioniert wie /caption_image, unterstützt jedoch nur das boxes-Format (nicht text0/text1).
     */
    ResponseImgFlip CaptionGif(string templateId, string username, string password, int maxFontSize, bool noWatermark, MemeCreationBox[] boxes);
    /*
     * Ermöglicht die Suche in über 1 Million Imgflip-Meme-Vorlagen. 
     * Für Autocomplete/Search-as-you-Type wird clientseitiges Caching empfohlen (kein globales Backend-Caching wegen ständiger Updates). 
     * Da die Datenbank nutzergeneriert und unkuratiert ist, empfiehlt sich eine zusätzliche Filterung (z. B. nach Sprache oder Beliebtheit).
     */
    Meme[] SearchMemes(string username, string password, string query, EMemeTyp type, bool includeNsfw);
    /*
     * Ruft ein Meme anhand seiner ID ab (gleiches Rückgabeformat wie /search_memes). 
     * Ideal, wenn Nutzer Vorlagen auf Imgflip hochladen und die ID direkt übergeben. 
     * (Nur mit Premium-Plan verfügbar)
     */
    Meme GetMeme(string username, string password,string templateId);
    /*
     * Erstellt automatisch ein passendes Meme aus einem eingegebenen Text. 
     * Ein neuronales Netzwerk wählt die geeignete Vorlage aus den Top 2.048 Memes aus 
     * und platziert den Text. Funktioniert am besten mit kurzen, 
     * einfachen Memes mit klaren Textmustern.
     */
    ResponseImgFlip AutoMeme(string username, string password, string text, bool noWatermark);
    /*
     * Generiert ein komplettes Meme von Grund auf mittels OpenAI GPT oder dem Imgflip-KI-Modell. 
     * Wichtig: Das klassische Imgflip-Modell basiert auf unkuratierten Nutzerdaten, 
     * ist unzensiert und kann anstößige Inhalte enthalten – eigene Inhalts- bzw. Sprachfilter werden empfohlen.
     */
    ResponseImgFlip AiMeme(string username, string password, EAiModel model, int templateId, string prefixText, bool noWatermark);
  }
}
