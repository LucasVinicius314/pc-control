package model

import (
	"desktop-client/pkg/enum"

	"github.com/mitchellh/mapstructure"
)

type AppEvent struct {
	Data interface{}       `json:"data"`
	Type enum.AppEventType `json:"type"`
}

func (e *AppEvent) GetGetFeedEventData() (*GetFeedEventData, error) {
	d := &GetFeedEventData{}
	if err := mapstructure.Decode(e.Data, d); err != nil {
		return nil, err
	}

	return d, nil
}

func (e *AppEvent) GetInteractEventData() (*InteractEventData, error) {
	d := &InteractEventData{}
	if err := mapstructure.Decode(e.Data, d); err != nil {
		return nil, err
	}

	return d, nil
}
