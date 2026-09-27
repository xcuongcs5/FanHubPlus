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
    ];

    protected static function booted(): void
    {
        static::creating(function (Event $event) {
            if (empty($event->id)) {
                $event->id = 'req_' . Str::lower(Str::random(12));
            }
            if (empty($event->status)) {
                $event->status = 'pending';
            }
        });
    }
}
