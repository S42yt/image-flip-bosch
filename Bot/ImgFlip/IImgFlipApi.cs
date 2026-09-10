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
    Dictionary<string, string> CaptionImage(string TemplateID, string Username,string Password,string Text0,string Text1,int MaxFontSize, bool NoWatermark, MemeCreationBox[] Boxes);
    /*
     * Fügt einer animierten GIF-Vorlage Text hinzu. 
     * Funktioniert wie /caption_image, unterstützt jedoch nur das boxes-Format (nicht text0/text1).
     */
    Dictionary<string, string> CaptionGif(string TemplateID, string Username, string Password, int MaxFontSize, bool NoWatermark, MemeCreationBox[] Boxes);
  }
}
