<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class Feedback extends Model
{
    use HasFactory;

    protected $table = 'cms_feedbacks';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'user_id',
        'user_name',
        'user_email',
        'type',
        'title',
        'content',
        'screenshot_url',
        'status',
        'response_note',
    ];

    protected static function booted(): void
    {
        static::creating(function (Feedback $item) {
            if (empty($item->id)) {
                $item->id = 'fb_' . Str::lower(Str::random(12));
            }
            if (empty($item->status)) {
                $item->status = 'Open';
            }
        });
    }
}
