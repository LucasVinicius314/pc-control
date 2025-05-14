package main

import (
	"context"
	"desktop-client/pkg/enum"
	"desktop-client/pkg/model"
	"desktop-client/pkg/util"
	"encoding/json"
	"fmt"
	"image"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"time"

	"gioui.org/app"
	"gioui.org/f32"
	"gioui.org/op"
	"gioui.org/op/paint"
	"gioui.org/unit"
	"github.com/getlantern/systray"
	"github.com/go-vgo/robotgo"
	"github.com/gorilla/websocket"
	"github.com/skip2/go-qrcode"
)

var upgrader = websocket.Upgrader{}

func main() {
	server := registerServer()

	gracefulShutdown(server)

	systray.Run(func() {
		if err := setupTray(); err != nil {
			log.Fatalf("failed to setup tray: %v", err)
		}

		log.Println("tray started")
	}, func() {
		log.Println("tray stopped")
		shutdown(server)
	})
}

func gracefulShutdown(server *http.Server) {
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, os.Interrupt)

	go func() {
		<-quit
		shutdown(server)
	}()
}

func handleConnection(w http.ResponseWriter, r *http.Request) {
	ws, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		fmt.Printf("failed to upgrade connection: %v", err)
		return
	}
	defer ws.Close()

	log.Printf("[%s] connected", ws.RemoteAddr().String())

connloop:
	for {
		_, msg, err := ws.ReadMessage()
		if err != nil {
			fmt.Printf("failed to read message: %v", err)
			break
		}

		// TODO: fix, logs
		// log.Printf("event (%s): %s", ws.RemoteAddr().String(), string(msg))

		event := &model.AppEvent{}
		if err := json.Unmarshal(msg, event); err != nil {
			fmt.Printf("failed to unmarshal event: %v", err)
			break
		}

		switch event.Type {
		case enum.GetFeed:
			eventData, err := event.GetGetFeedEventData()
			if err != nil {
				fmt.Printf("failed to get get feed event data: %v", err)
				break
			}

			robotgo.Move(int(eventData.X), int(eventData.Y))

			data, err := util.CaptureScreenshot(int(eventData.X), int(eventData.Y), int(eventData.Width), int(eventData.Height))
			if err != nil {
				fmt.Printf("failed to capture screenshot: %v", err)
				break
			}

			err = util.SendEvent(ws, model.AppEvent{
				Data: map[string]interface{}{
					"content": data,
				},
				Type: enum.GetFeed,
			})
			if err != nil {
				fmt.Printf("failed to send event: %v", err)
				break connloop
			}
		case enum.Interact:
			eventData, err := event.GetInteractEventData()
			if err != nil {
				fmt.Printf("failed to get interact event data: %v", err)
				break
			}

			switch eventData.Code {
			case "lmb":
				robotgo.Click("left", false)
			case "rmb":
				robotgo.Click("right", false)
			}

			err = util.SendEvent(ws, model.AppEvent{
				Data: map[string]interface{}{
					"message": "ok",
				},
				Type: enum.Interact,
			})
			if err != nil {
				fmt.Printf("failed to send event: %v", err)
				break connloop
			}
		}
	}
}

func registerServer() *http.Server {
	mux := http.NewServeMux()

	mux.HandleFunc("/ws", handleConnection)

	server := &http.Server{
		Addr:    ":48123",
		Handler: mux,
	}

	go func() {
		if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("listen: %s", err)
		}
	}()

	fmt.Println("server is listening on port 48123")

	return server
}

func setupTray() error {
	iconData, err := os.ReadFile("assets/icon.ico")
	if err != nil {
		return fmt.Errorf("failed to read icon: %v", err)
	}

	systray.SetIcon(iconData)
	systray.SetTitle("PC Control")
	systray.SetTooltip("PC Control.")

	mShowQRCode := systray.AddMenuItem("Show QR Code", "Show QR Code")
	go func() {
		<-mShowQRCode.ClickedCh
		if err := showQRCode(); err != nil {
			log.Printf("failed to show qr code: %v", err)
		}
	}()
	mQuit := systray.AddMenuItem("Quit", "Stop PC Control")
	go func() {
		<-mQuit.ClickedCh
		systray.Quit()
	}()

	return nil
}

func shutdown(server *http.Server) {
	log.Println("shutting down server...")

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := server.Shutdown(ctx); err != nil {
		log.Fatalf("server forced to shutdown: %v", err)
	}

	log.Println("server stopped")
}

func getLocalIP() (string, error) {
	addrs, err := net.InterfaceAddrs()
	if err != nil {
		return "", err
	}

	for _, addr := range addrs {
		ipNet, ok := addr.(*net.IPNet)
		if ok && !ipNet.IP.IsLoopback() {
			if ipNet.IP.To4() != nil && ipNet.IP.IsPrivate() {
				return ipNet.IP.String(), nil
			}
		}
	}

	return "", fmt.Errorf("no private IP found")
}

func showQRCode() error {
	ip, err := getLocalIP()
	if err != nil {
		return fmt.Errorf("failed to get local ip: %v", err)
	}

	deepLink := fmt.Sprintf("pccontrol://connect?ipAddress=%s", ip)

	qr, err := qrcode.New(deepLink, qrcode.Medium)
	if err != nil {
		return fmt.Errorf("failed to create qr code: %v", err)
	}

	w := app.Window{}
	w.Option(app.Size(unit.Dp(300), unit.Dp(300)), app.Title("QR Code"))

	var ops op.Ops
	for {
		switch e := w.Event().(type) {
		case app.DestroyEvent:
			return e.Err
		case app.FrameEvent:
			gtx := app.NewContext(&ops, e)

			drawImage(&ops, qr.Image(256), 300)

			e.Frame(gtx.Ops)
		}
	}
}

func drawImage(ops *op.Ops, img image.Image, size float32) {
	imageOp := paint.NewImageOp(img)
	imageOp.Filter = paint.FilterNearest
	imageOp.Add(ops)
	op.Affine(f32.Affine2D{}.Scale(f32.Pt(0, 0), f32.Pt(size/float32(img.Bounds().Dx()), size/float32(img.Bounds().Dy())))).Add(ops)
	paint.PaintOp{}.Add(ops)
}
