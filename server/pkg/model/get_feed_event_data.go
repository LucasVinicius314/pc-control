package model

type GetFeedEventData struct {
	Height int64 `json:"height"`
	Width  int64 `json:"width"`
	X      int64 `json:"x"`
	Y      int64 `json:"y"`
}
