package util

import (
	"bytes"
	"encoding/json"
	"fmt"
	"image"
	"image/jpeg"

	"github.com/gorilla/websocket"
	"github.com/kbinani/screenshot"
)

func CaptureScreenshot(x int, y int, width int, height int) ([]byte, error) {
	bounds := image.Rectangle{
		Min: image.Point{X: x - width/2, Y: y - height/2},
		Max: image.Point{X: x + width/2, Y: y + height/2},
	}

	img, err := screenshot.CaptureRect(bounds)
	if err != nil {
		return nil, fmt.Errorf("failed to capture rect: %v", err)
	}

	var buf bytes.Buffer
	if err := jpeg.Encode(&buf, img, &jpeg.Options{Quality: 40}); err != nil {
		return nil, fmt.Errorf("failed to encode image: %v", err)
	}

	return buf.Bytes(), nil
}

func SendEvent(ws *websocket.Conn, value interface{}) error {
	data, err := json.Marshal(value)
	if err != nil {
		return fmt.Errorf("failed to marshal value: %v", err)
	}

	if err := ws.WriteMessage(websocket.TextMessage, data); err != nil {
		return fmt.Errorf("failed to write message: %v", err)
	}

	return nil
}
