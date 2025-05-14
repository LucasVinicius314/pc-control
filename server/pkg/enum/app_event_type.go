package enum

type AppEventType string

const (
	GetFeed  AppEventType = "get-feed"
	Interact AppEventType = "interact"
)
