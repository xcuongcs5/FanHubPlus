<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class Event extends Model
{
    use HasFactory;

    protected $table = 'events';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'title',
        'description',
        'status',
        'organizer',
        'organizer_email',
        'location',
        'start_time',
        'end_time',
        'admin_note',
        'ai_risk_score',
        'ticket_types_json',
    ];

    protected static function booted(): void
    {
        static::creating(function (Event $event) {
            if (empty($event->id)) {
                $event->id = 'evt_' . Str::lower(Str::random(12));
            }
            if (empty($event->status)) {
                $event->status = 'Pending';
            }
            if (!isset($event->ai_risk_score)) {
                $event->ai_risk_score = 0.05;
            }
        });
    }
}
